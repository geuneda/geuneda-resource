using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	[TestFixture]
	public class ResourceCacheTest
	{
		private FakeFileServer _server;
		private InlineResourceWorker _worker;
		private string _root;
		private ILogSink _originalSink;
		private List<LogEntry> _logs;

		private sealed class ListSink : ILogSink
		{
			private readonly List<LogEntry> _entries;

			public ListSink(List<LogEntry> entries)
			{
				_entries = entries;
			}

			public void Write(in LogEntry entry)
			{
				_entries.Add(entry);
			}
		}

		[SetUp]
		public void SetUp()
		{
			_originalSink = Log.Sink;
			_logs = new List<LogEntry>();
			Log.Sink = new ListSink(_logs);
			_server = new FakeFileServer();
			_worker = new InlineResourceWorker();
			_root = Path.Combine(Path.GetTempPath(), "geuneda-resource-test-" + Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;

			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, true);
			}
		}

		private ResourceCache CreateCache(int maxConcurrent = 2)
		{
			return new ResourceCache(new ResourceCacheOptions
			{
				HttpClient = _server.CreateClient(),
				RootPath = _root,
				MaxConcurrentDownloads = maxConcurrent,
				RequestTimeout = TimeSpan.FromSeconds(5)
			}, _worker);
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		private string PathOf(ResourceEntry entry)
		{
			return Path.Combine(_root, entry.Category, $"{entry.Version}.{entry.Sha256.Substring(0, 16)}.bin");
		}

		private string[] FilesIn(string category)
		{
			return Directory.GetFiles(Path.Combine(_root, category)).Select(Path.GetFileName).ToArray();
		}

		private ResourceCache CreateCache(IResourceWorker worker, int maxConcurrent = 2)
		{
			return new ResourceCache(new ResourceCacheOptions { HttpClient = _server.CreateClient(), RootPath = _root, MaxConcurrentDownloads = maxConcurrent }, worker);
		}

		[Test]
		public void Constructor_InvalidOptions_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new ResourceCache(null));
			Assert.Throws<ArgumentException>(() => new ResourceCache(new ResourceCacheOptions { RootPath = _root }));
			Assert.Throws<ArgumentException>(() => new ResourceCache(new ResourceCacheOptions { HttpClient = _server.CreateClient(), RootPath = _root, MaxConcurrentDownloads = 0 }));
			Assert.Throws<ArgumentException>(() => new ResourceCache(new ResourceCacheOptions { HttpClient = _server.CreateClient(), RootPath = _root, RequestTimeout = TimeSpan.Zero }));
			Assert.Throws<ArgumentException>(() => new ResourceCache(new ResourceCacheOptions { HttpClient = _server.CreateClient(), RootPath = "" }));
		}

		[Test]
		public void Download_SavesVerifiedFile_ThenLoadAndPathWork_WithoutTemporaryFiles()
		{
			var tables = _server.Publish("tables", "v1", "{\"units\":[1,2,3]}");
			var cache = CreateCache();
			var progress = new List<ResourceDownloadProgress>();

			var result = Run(cache.DownloadAsync(new[] { tables }, progress.Add));
			var bytes = Run(cache.LoadBytesAsync(tables));
			var path = Run(cache.GetFilePathAsync(tables));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual("{\"units\":[1,2,3]}", Encoding.UTF8.GetString(bytes.Value));
			Assert.AreEqual(PathOf(tables), path.Value);
			CollectionAssert.AreEqual(new[] { Path.GetFileName(PathOf(tables)) }, FilesIn("tables"));
			StringAssert.StartsWith("v1.", Path.GetFileName(path.Value));
			Assert.AreEqual(new ResourceDownloadProgress(0, 1, 0, tables.Size), progress[0]);
			Assert.AreEqual(new ResourceDownloadProgress(1, 1, tables.Size, tables.Size), progress[1]);
			Assert.AreEqual(TimeSpan.FromSeconds(5), _server.Requests[0].Timeout);
		}

		[Test]
		public void Check_ListsOnlyMissingOrBrokenFiles_AndDownloadSkipsValidOnes()
		{
			var tables = _server.Publish("tables", "v1", "tables");
			var words = _server.Publish("banwords", "v3", "words");
			var cache = CreateCache();
			Run(cache.DownloadAsync(new[] { tables }));

			var check = Run(cache.CheckAsync(new[] { tables, words }));

			CollectionAssert.AreEqual(new[] { words }, check.Value.Missing);
			Assert.AreEqual(words.Size, check.Value.TotalBytes);
			Assert.IsFalse(check.Value.IsUpToDate);

			Run(cache.DownloadAsync(new[] { tables, words }));

			Assert.AreEqual(1, _server.Count(tables));
			Assert.AreEqual(1, _server.Count(words));
			Assert.IsTrue(Run(cache.CheckAsync(new[] { tables, words })).Value.IsUpToDate);
		}

		[Test]
		public void Check_AfterRestart_HashesTheFile_AndRedownloadsTamperedFiles()
		{
			var tables = _server.Publish("tables", "v1", "tables-original");
			Run(CreateCache().DownloadAsync(new[] { tables }));
			File.WriteAllText(PathOf(tables), "tables-tampered");

			// 새 실행: 이 실행에서 확인한 기록이 없으니 해시를 다시 본다(같은 크기 변조도 잡음)
			var restarted = CreateCache();
			var check = Run(restarted.CheckAsync(new[] { tables }));

			CollectionAssert.AreEqual(new[] { tables }, check.Value.Missing);
			Assert.IsTrue(File.Exists(PathOf(tables)), "checks never delete files");

			Assert.IsTrue(Run(restarted.DownloadAsync(new[] { tables })).IsSuccess);
			Assert.AreEqual("tables-original", File.ReadAllText(PathOf(tables)));
		}

		[Test]
		public void Download_NewVersion_DeletesOlderVersionsOnlyAfterSaving()
		{
			var v1 = _server.Publish("tables", "v1", "one");
			var v2 = _server.Publish("tables", "v2", "two");
			var cache = CreateCache();
			Run(cache.DownloadAsync(new[] { v1 }));
			File.WriteAllText(Path.Combine(_root, "tables", ".v2.stale.tmp"), "left by a crash");

			Run(cache.DownloadAsync(new[] { v2 }));

			CollectionAssert.AreEqual(new[] { Path.GetFileName(PathOf(v2)) }, FilesIn("tables"));
			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.LoadBytesAsync(v1)).Code);
		}

		[TestCase("size")]
		[TestCase("hash")]
		public void Download_IntegrityMismatch_SavesNothing_AndKeepsTheOldVersion(string kind)
		{
			var v1 = _server.Publish("tables", "v1", "one");
			var v2 = _server.Publish("tables", "v2", "two");
			var cache = CreateCache();
			Run(cache.DownloadAsync(new[] { v1 }));
			_server.Files[v2.Url.AbsoluteUri] = Encoding.UTF8.GetBytes(kind == "size" ? "two-longer" : "TWO");

			var result = Run(cache.DownloadAsync(new[] { v2 }));

			Assert.AreEqual(ResourceResultCodes.IntegrityFailed, result.Code);
			CollectionAssert.AreEqual(new[] { Path.GetFileName(PathOf(v1)) }, FilesIn("tables"));
			Assert.IsTrue(Run(cache.LoadBytesAsync(v1)).IsSuccess);
		}

		[Test]
		public void Download_HttpAndTransportFailures_AreReported_AndOtherFilesStillDownload()
		{
			var missing = new ResourceEntry("tables", "v1", 1, FakeFileServer.Sha256(new byte[] { 1 }), FakeFileServer.BaseAddress + "nothing");
			var offline = _server.Publish("banwords", "v1", "words");
			var fine = _server.Publish("strings", "v1", "strings");
			_server.Failures[offline.Url.AbsoluteUri] = Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");

			var result = Run(CreateCache(1).DownloadAsync(new[] { missing, offline, fine }));

			Assert.AreEqual(HttpResultCodes.HttpError, result.Code);
			StringAssert.Contains("2 of 3", result.Message);
			StringAssert.Contains("tables@v1", result.Message);
			StringAssert.Contains("banwords@v1", result.Message);
			Assert.IsTrue(File.Exists(PathOf(fine)));
		}

		[Test]
		public void Download_RespectsTheConcurrencyLimit()
		{
			var entries = Enumerable.Range(0, 5).Select(i => _server.Publish("c" + i, "v1", "content" + i)).ToArray();

			foreach (var entry in entries)
			{
				_server.Gates[entry.Url.AbsoluteUri] = new UniTaskCompletionSource();
			}

			var download = CreateCache(2).DownloadAsync(entries);
			Assert.AreEqual(2, _server.Requests.Count);

			foreach (var entry in entries)
			{
				_server.Gates[entry.Url.AbsoluteUri].TrySetResult();
			}

			Assert.IsTrue(Run(download).IsSuccess);
			Assert.AreEqual(5, _server.Requests.Count);
			Assert.AreEqual(2, _server.MaxInFlight);
		}

		[Test]
		public void Download_WhileAnotherRuns_IsInProgress()
		{
			var tables = _server.Publish("tables", "v1", "tables");
			_server.Gates[tables.Url.AbsoluteUri] = new UniTaskCompletionSource();
			var cache = CreateCache();

			var first = cache.DownloadAsync(new[] { tables });
			var second = Run(cache.DownloadAsync(new[] { tables }));

			Assert.AreEqual(ResultCodes.InProgress, second.Code);
			_server.Gates[tables.Url.AbsoluteUri].TrySetResult();
			Assert.IsTrue(Run(first).IsSuccess);
			Assert.IsTrue(Run(cache.DownloadAsync(new[] { tables })).IsSuccess, "the next download may start after the first ends");
		}

		[Test]
		public void Download_Cancelled_StopsPendingFiles_AndKeepsFinishedOnes()
		{
			var first = _server.Publish("a", "v1", "a");
			var second = _server.Publish("b", "v1", "b");
			var third = _server.Publish("c", "v1", "c");
			_server.Gates[second.Url.AbsoluteUri] = new UniTaskCompletionSource();
			var cancellation = new CancellationTokenSource();

			var download = CreateCache(1).DownloadAsync(new[] { first, second, third }, null, cancellation.Token);
			cancellation.Cancel();

			var result = Run(download);

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.IsTrue(File.Exists(PathOf(first)));
			Assert.IsFalse(File.Exists(PathOf(second)));
			Assert.AreEqual(0, _server.Count(third));
		}

		[Test]
		public void Download_SameCategoryWithTwoVersions_Throws_AndDuplicatesAreMerged()
		{
			var v1 = _server.Publish("tables", "v1", "one");
			var v2 = _server.Publish("tables", "v2", "two");
			var cache = CreateCache();

			Assert.Throws<ArgumentException>(() => cache.DownloadAsync(new[] { v1, v2 }));
			Assert.Throws<ArgumentNullException>(() => cache.CheckAsync(new ResourceEntry[] { null }));
			Assert.IsTrue(Run(cache.DownloadAsync(new[] { v1, v1 })).IsSuccess);
			Assert.AreEqual(1, _server.Count(v1));
		}

		[Test]
		public void Download_UnwritableRoot_IsStorageFailed()
		{
			File.WriteAllText(_root, "a file where the cache folder should be");

			try
			{
				var tables = _server.Publish("tables", "v1", "tables");

				var result = Run(CreateCache().DownloadAsync(new[] { tables }));

				Assert.AreEqual(ResourceResultCodes.StorageFailed, result.Code);
			}
			finally
			{
				File.Delete(_root);
			}
		}

		[Test]
		public void Load_MissingOrBrokenFile_IsNotCached_WithoutDeletingIt_AndTheNextSaveReplacesIt()
		{
			var tables = _server.Publish("tables", "v1", "tables");
			var cache = CreateCache();

			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.LoadBytesAsync(tables)).Code);
			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.GetFilePathAsync(tables)).Code);

			Run(cache.DownloadAsync(new[] { tables }));
			File.WriteAllText(PathOf(tables), "TABLES");

			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.LoadBytesAsync(tables)).Code);
			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.GetFilePathAsync(tables)).Code);
			Assert.IsTrue(File.Exists(PathOf(tables)), "queries never delete files");

			Assert.IsTrue(Run(cache.DownloadAsync(new[] { tables })).IsSuccess);
			Assert.AreEqual("tables", Encoding.UTF8.GetString(Run(cache.LoadBytesAsync(tables)).Value));
		}

		[Test]
		public void Query_RacingADownload_DoesNotDeleteTheFileItSaves()
		{
			// 리뷰 재현: 다운로드가 응답을 기다리는 동안 조회가 파일 없음을 보고, 그 뒤의 작업이 저장 뒤에 돌아도 저장한 파일이 남아야 한다
			var worker = new DeferredResourceWorker();
			var tables = _server.Publish("tables", "v1", "tables");
			_server.Gates[tables.Url.AbsoluteUri] = new UniTaskCompletionSource();
			var cache = CreateCache(worker);

			var download = cache.DownloadAsync(new[] { tables });
			worker.RunAllNewestFirst();
			var load = cache.LoadBytesAsync(tables);
			worker.RunOldest();
			_server.Gates[tables.Url.AbsoluteUri].TrySetResult();
			worker.RunAllNewestFirst();

			Assert.AreEqual(ResourceResultCodes.NotCached, Run(load).Code);
			Assert.IsTrue(Run(download).IsSuccess);
			Assert.IsTrue(File.Exists(PathOf(tables)));
			Assert.AreEqual(0, worker.PendingCount);
		}

		[Test]
		public void SameVersionWithNewContent_KeepsTheOldFileUntilTheNewOneIsSaved()
		{
			var oldEntry = _server.Publish("tables", "v1", "old content");
			var cache = CreateCache();
			Run(cache.DownloadAsync(new[] { oldEntry }));
			var oldPath = Run(cache.GetFilePathAsync(oldEntry)).Value;
			var newEntry = _server.Publish("tables", "v1", "new content!");

			Assert.IsFalse(Run(cache.CheckAsync(new[] { newEntry })).Value.IsUpToDate);
			Assert.IsTrue(Run(cache.LoadBytesAsync(oldEntry)).IsSuccess, "checking the new content keeps the old file");

			_server.Files[newEntry.Url.AbsoluteUri] = Encoding.UTF8.GetBytes("broken conten");
			Assert.AreEqual(ResourceResultCodes.IntegrityFailed, Run(cache.DownloadAsync(new[] { newEntry })).Code);
			Assert.IsTrue(Run(cache.LoadBytesAsync(oldEntry)).IsSuccess, "a failed replacement keeps the old file");

			_server.Files[newEntry.Url.AbsoluteUri] = Encoding.UTF8.GetBytes("new content!");
			Assert.IsTrue(Run(cache.DownloadAsync(new[] { newEntry })).IsSuccess);
			Assert.AreEqual("new content!", Encoding.UTF8.GetString(Run(cache.LoadBytesAsync(newEntry)).Value));
			Assert.AreNotEqual(oldPath, Run(cache.GetFilePathAsync(newEntry)).Value, "new content of the same version gets its own file name");
			Assert.AreEqual(ResourceResultCodes.NotCached, Run(cache.LoadBytesAsync(oldEntry)).Code);
			CollectionAssert.AreEqual(new[] { Path.GetFileName(PathOf(newEntry)) }, FilesIn("tables"));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void WorkerException_IsRethrownOnlyAfterEveryWorkerEnds(bool throwingCancelCallback)
		{
			var slow = _server.Publish("slow", "v1", "slow");
			var broken = _server.Publish("broken", "v1", "broken");
			_server.Gates[slow.Url.AbsoluteUri] = new UniTaskCompletionSource();
			_server.IgnoreCancellation = true;
			_server.ThrowOnCancel = throwingCancelCallback;
			_server.Throws.Add(broken.Url.AbsoluteUri);
			var cache = CreateCache();

			var download = cache.DownloadAsync(new[] { slow, broken });

			Assert.IsFalse(download.Status.IsCompleted(), "the slow worker is still running");
			Assert.AreEqual(ResultCodes.InProgress, Run(cache.DownloadAsync(new[] { slow })).Code);

			_server.Gates[slow.Url.AbsoluteUri].TrySetResult();

			Assert.Throws<ArgumentException>(() => download.GetAwaiter().GetResult());
			Assert.AreEqual(throwingCancelCallback, _logs.Exists(entry => entry.Level == LogLevel.Error && entry.Message.Contains("cancellation callback")));
			_server.Throws.Clear();
			Assert.IsTrue(Run(cache.DownloadAsync(new[] { slow, broken })).IsSuccess);
		}

		[Test]
		public void Save_ReusesAValidFileThatCouldNotBeReadDuringTheCheck()
		{
			// 리뷰 재현: 판정 때 파일을 읽지 못해(잠김) 받게 되어도, 저장할 때 같은 이름의 정상 파일이면 지우거나 덮어쓰지 않는다
			var tables = _server.Publish("tables", "v1", "tables");
			Run(CreateCache().DownloadAsync(new[] { tables }));
			var written = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			File.SetLastWriteTimeUtc(PathOf(tables), written);

			var worker = new FailingResourceWorker { FailuresLeft = 1 };
			var result = Run(CreateCache(worker).DownloadAsync(new[] { tables }));

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreEqual(2, _server.Count(tables), "the unreadable file was downloaded again");
			Assert.AreEqual(written, File.GetLastWriteTimeUtc(PathOf(tables)), "the existing valid file was kept, not replaced");
			CollectionAssert.AreEqual(new[] { Path.GetFileName(PathOf(tables)) }, FilesIn("tables"));
		}

		[Test]
		public void Cancellation_IsHonouredBeforeAndAfterFileWork()
		{
			var tables = _server.Publish("tables", "v1", "tables");
			var cancelled = new CancellationToken(true);
			var cache = CreateCache();

			Assert.AreEqual(ResultCodes.Cancelled, Run(cache.CheckAsync(Array.Empty<ResourceEntry>(), cancelled)).Code);
			Assert.AreEqual(ResultCodes.Cancelled, Run(cache.LoadBytesAsync(tables, cancelled)).Code);
			Assert.AreEqual(ResultCodes.Cancelled, Run(cache.GetFilePathAsync(tables, cancelled)).Code);
			Assert.AreEqual(0, _worker.Runs, "no file work after cancellation");

			var worker = new DeferredResourceWorker();
			var deferred = CreateCache(worker);
			var source = new CancellationTokenSource();
			var check = deferred.CheckAsync(new[] { tables }, source.Token);
			source.Cancel();
			worker.RunOldest();

			Assert.AreEqual(ResultCodes.Cancelled, Run(check).Code, "cancelled while checking the last entry");
		}

		[Test]
		public void ProgressCallbackThrows_DownloadStillSucceeds()
		{
			var tables = _server.Publish("tables", "v1", "tables");

			var result = Run(CreateCache().DownloadAsync(new[] { tables }, _ => throw new InvalidOperationException("boom")));

			Assert.IsTrue(result.IsSuccess);
			Assert.IsTrue(_logs.Exists(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException));
		}
	}
}
