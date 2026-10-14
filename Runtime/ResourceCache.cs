using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>
	/// 기본 원격 리소스 캐시입니다. 부트스트랩에서 만들어 <see cref="IResourceCache"/> 로 바인딩합니다. 동작 규칙은 <see cref="IResourceCache"/> 를 봅니다.
	/// </summary>
	public sealed class ResourceCache : IResourceCache
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Resource");

		private readonly IHttpClient _http;
		private readonly string _root;
		private readonly int _maxConcurrentDownloads;
		private readonly TimeSpan _requestTimeout;
		private readonly IResourceWorker _worker;

		// 이 실행에서 해시까지 확인한 파일. 다시 확인할 때 해시를 건너뛴다(파일이 있는지는 매번 본다)
		private readonly Dictionary<string, ResourceEntry> _verified = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);

		private bool _downloading;

		/// <summary>메인 스레드에서 만듭니다(<c>Application.persistentDataPath</c> 를 읽음).</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">HttpClient 가 없거나, 동시 다운로드 수가 1 보다 작거나, 제한 시간이 0 이하이거나, 캐시 폴더가 빈 문자열인 경우</exception>
		public ResourceCache(ResourceCacheOptions options) : this(options, new ThreadPoolResourceWorker())
		{
		}

		internal ResourceCache(ResourceCacheOptions options, IResourceWorker worker)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.HttpClient == null || options.MaxConcurrentDownloads < 1 || options.RequestTimeout <= TimeSpan.Zero || options.RootPath?.Length == 0)
			{
				throw new ArgumentException("HttpClient is required, MaxConcurrentDownloads must be at least 1, RequestTimeout positive and RootPath not empty", nameof(options));
			}

			_http = options.HttpClient;
			_root = Path.GetFullPath(options.RootPath ?? Path.Combine(Application.persistentDataPath, "geuneda-resources"));
			_maxConcurrentDownloads = options.MaxConcurrentDownloads;
			_requestTimeout = options.RequestTimeout;
			_worker = worker;
		}

		/// <summary>캐시 폴더(절대 경로)입니다.</summary>
		public string RootPath => _root;

		public UniTask<Result<ResourceCheck>> CheckAsync(IEnumerable<ResourceEntry> entries, CancellationToken cancellationToken = default)
		{
			return CheckCoreAsync(Distinct(entries), cancellationToken);
		}

		public UniTask<Result> DownloadAsync(IEnumerable<ResourceEntry> entries, Action<ResourceDownloadProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			var distinct = Distinct(entries);

			if (_downloading)
			{
				return UniTask.FromResult(Result.Fail(ResultCodes.InProgress, "Another resource download is running"));
			}

			return DownloadCoreAsync(distinct, progress, cancellationToken);
		}

		public UniTask<Result<byte[]>> LoadBytesAsync(ResourceEntry entry, CancellationToken cancellationToken = default)
		{
			return LoadBytesCoreAsync(entry ?? throw new ArgumentNullException(nameof(entry)), cancellationToken);
		}

		public UniTask<Result<string>> GetFilePathAsync(ResourceEntry entry, CancellationToken cancellationToken = default)
		{
			return GetFilePathCoreAsync(entry ?? throw new ArgumentNullException(nameof(entry)), cancellationToken);
		}

		private async UniTask<Result<ResourceCheck>> CheckCoreAsync(IReadOnlyList<ResourceEntry> entries, CancellationToken cancellationToken)
		{
			var missing = new List<ResourceEntry>();

			foreach (var entry in entries)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return Cancelled<ResourceCheck>("Resource check was cancelled");
				}

				if (!await IsCachedAsync(entry))
				{
					missing.Add(entry);
				}
			}

			// 빈 목록이나 마지막 항목을 확인하는 동안 취소된 경우도 취소로 끝낸다
			return cancellationToken.IsCancellationRequested
				? Cancelled<ResourceCheck>("Resource check was cancelled")
				: Result<ResourceCheck>.Ok(new ResourceCheck(missing, missing.Sum(entry => entry.Size)));
		}

		private async UniTask<Result> DownloadCoreAsync(IReadOnlyList<ResourceEntry> entries, Action<ResourceDownloadProgress> progress,
			CancellationToken cancellationToken)
		{
			_downloading = true;

			try
			{
				var check = await CheckCoreAsync(entries, cancellationToken);

				if (!check.IsSuccess)
				{
					return check;
				}

				var queue = check.Value.Missing;
				var next = 0;
				var completedFiles = 0;
				var completedBytes = 0L;
				var failures = new List<Result>();
				Exception thrown = null;

				Report(progress, new ResourceDownloadProgress(0, queue.Count, 0, check.Value.TotalBytes));

				// 예외가 나면 다른 워커를 멈추고, 모든 워커가 끝난 뒤에 예외를 다시 던진다(그 전에 _downloading 을 풀지 않게)
				using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					// 메인 스레드에서만 돌아 next 를 나눠 써도 된다(파일 입출력만 스레드 풀)
					async UniTask Worker()
					{
						try
						{
							while (next < queue.Count && !stop.IsCancellationRequested)
							{
								var entry = queue[next++];
								var result = await DownloadOneAsync(entry, stop.Token);

								if (!result.IsSuccess)
								{
									failures.Add(result);
									continue;
								}

								completedFiles++;
								completedBytes += entry.Size;
								Report(progress, new ResourceDownloadProgress(completedFiles, queue.Count, completedBytes, check.Value.TotalBytes));
							}
						}
						catch (Exception exception)
						{
							thrown = thrown ?? exception;

							// 취소 콜백이 던져도 이 워커 밖으로 내보내지 않는다(WhenAll 이 다른 워커를 기다리지 않고 끝나게 됨)
							try
							{
								stop.Cancel();
							}
							catch (Exception cancelFailure)
							{
								Logger.Error(cancelFailure, "A cancellation callback failed while stopping resource downloads");
							}
						}
					}

					var workers = Math.Min(_maxConcurrentDownloads, queue.Count);
					await UniTask.WhenAll(Enumerable.Range(0, workers).Select(_ => Worker()));
				}

				if (thrown != null)
				{
					ExceptionDispatchInfo.Capture(thrown).Throw();
				}

				// 취소가 아닌 실패가 있으면 첫 실패의 코드로, 취소만 있었으면 Cancelled 로 끝낸다
				var errors = failures.Where(result => result.Code != ResultCodes.Cancelled).ToList();

				if (errors.Count > 0)
				{
					return Result.Fail(errors[0].Code, $"{errors.Count} of {queue.Count} resources failed: {string.Join("; ", errors.Select(result => result.Message))}");
				}

				if (cancellationToken.IsCancellationRequested || failures.Count > 0)
				{
					return Result.Fail(ResultCodes.Cancelled, $"Resource download was cancelled after {completedFiles} of {queue.Count} files");
				}

				return Result.Ok();
			}
			finally
			{
				_downloading = false;
			}
		}

		private async UniTask<Result> DownloadOneAsync(ResourceEntry entry, CancellationToken cancellationToken)
		{
			var sent = await _http.SendAsync(HttpRequest.Get(entry.Url.AbsoluteUri).WithTimeout(_requestTimeout), cancellationToken);

			if (!sent.IsSuccess)
			{
				return Result.Fail(sent.Code, $"{entry}: {sent.Message}");
			}

			var response = sent.Value;

			if (!response.IsSuccessStatusCode)
			{
				return Result.Fail(HttpResultCodes.HttpError, $"{entry}: status {response.StatusCode} from {response.Uri}");
			}

			var body = response.Body;

			var mismatch = await _worker.Run(() => ResourceFiles.Mismatch(body, entry));

			if (mismatch != null)
			{
				Logger.Warning($"Rejected {entry} from {response.Uri}: {mismatch}");
				return Result.Fail(ResourceResultCodes.IntegrityFailed, $"{entry}: downloaded {mismatch}");
			}

			IReadOnlyList<string> leftovers;

			try
			{
				leftovers = await _worker.Run(() => ResourceFiles.Commit(_root, entry, body));
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				Logger.Warning($"Could not save {entry}: {exception.Message}");
				return Result.Fail(ResourceResultCodes.StorageFailed, $"{entry}: {exception.Message}");
			}

			foreach (var leftover in leftovers)
			{
				Logger.Warning($"Could not delete the old resource file {leftover}; it is removed on the next save");
			}

			_verified[entry.Category] = entry;
			Logger.Info($"Saved {entry} ({entry.Size} bytes)");
			return Result.Ok();
		}

		private async UniTask<Result<byte[]>> LoadBytesCoreAsync(ResourceEntry entry, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Cancelled<byte[]>("Resource load was cancelled");
			}

			var path = ResourceFiles.PathOf(_root, entry);
			byte[] bytes;

			try
			{
				bytes = await _worker.Run(() =>
				{
					var read = ResourceFiles.ReadIfExists(path);
					return read != null && ResourceFiles.Matches(read, entry) ? read : null;
				});
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return Result<byte[]>.Fail(ResourceResultCodes.StorageFailed, $"{entry}: {exception.Message}");
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return Cancelled<byte[]>("Resource load was cancelled");
			}

			if (bytes == null)
			{
				// 지우지 않는다. 맞지 않는 파일은 다음 저장이 정리한다
				Forget(entry);
				return Result<byte[]>.Fail(ResourceResultCodes.NotCached, $"{entry} is not cached");
			}

			_verified[entry.Category] = entry;
			return Result<byte[]>.Ok(bytes);
		}

		private async UniTask<Result<string>> GetFilePathCoreAsync(ResourceEntry entry, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Cancelled<string>("Resource lookup was cancelled");
			}

			var cached = await IsCachedAsync(entry);

			if (cancellationToken.IsCancellationRequested)
			{
				return Cancelled<string>("Resource lookup was cancelled");
			}

			return cached
				? Result<string>.Ok(ResourceFiles.PathOf(_root, entry))
				: Result<string>.Fail(ResourceResultCodes.NotCached, $"{entry} is not cached");
		}

		private static Result<T> Cancelled<T>(string message)
		{
			return Result<T>.Fail(ResultCodes.Cancelled, message);
		}

		/// <summary>
		/// 캐시 파일이 맞으면 true 입니다. 파일을 바꾸지 않습니다(맞지 않는 파일은 다음 저장이 지움). 읽지 못하면(입출력 오류) 받아야 하는 것으로 봅니다.
		/// 조회가 파일을 지우면 판정과 삭제 사이에 저장된 새 파일을 지울 수 있어서다.
		/// </summary>
		private async UniTask<bool> IsCachedAsync(ResourceEntry entry)
		{
			var path = ResourceFiles.PathOf(_root, entry);
			var verified = _verified.TryGetValue(entry.Category, out var known) && known.IsSameFile(entry);
			bool valid;

			try
			{
				valid = await _worker.Run(() => verified ? File.Exists(path) : ResourceFiles.IsValid(path, entry));
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				Logger.Warning($"Could not check {entry}: {exception.Message}");
				valid = false;
			}

			if (valid)
			{
				_verified[entry.Category] = entry;
			}
			else
			{
				Forget(entry);
			}

			return valid;
		}

		private void Forget(ResourceEntry entry)
		{
			if (_verified.TryGetValue(entry.Category, out var known) && known.IsSameFile(entry))
			{
				_verified.Remove(entry.Category);
			}
		}

		private static IReadOnlyList<ResourceEntry> Distinct(IEnumerable<ResourceEntry> entries)
		{
			if (entries == null)
			{
				throw new ArgumentNullException(nameof(entries));
			}

			var byCategory = new Dictionary<string, ResourceEntry>(StringComparer.Ordinal);
			var ordered = new List<ResourceEntry>();

			foreach (var entry in entries)
			{
				if (entry == null)
				{
					throw new ArgumentNullException(nameof(entries), "Resource entries must not be null");
				}

				if (byCategory.TryGetValue(entry.Category, out var existing))
				{
					if (!existing.IsSameFile(entry))
					{
						throw new ArgumentException($"Category '{entry.Category}' is listed with two different files ({existing.Version}, {entry.Version})", nameof(entries));
					}

					continue;
				}

				byCategory.Add(entry.Category, entry);
				ordered.Add(entry);
			}

			return ordered;
		}

		private static void Report(Action<ResourceDownloadProgress> progress, ResourceDownloadProgress value)
		{
			if (progress == null)
			{
				return;
			}

			try
			{
				progress(value);
			}
			catch (Exception exception)
			{
				Logger.Error(exception);
			}
		}
	}
}
