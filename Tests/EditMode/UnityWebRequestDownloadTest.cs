using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	/// <summary>
	/// 로컬 <see cref="HttpListener"/> 서버에서 실제 UnityWebRequest 로 받고, 실제 스레드 풀에서 해시·저장하는 경로를 확인합니다.
	/// </summary>
	public class UnityWebRequestDownloadTest
	{
		private sealed class FileServer : IDisposable
		{
			private readonly HttpListener _listener = new HttpListener();
			private readonly byte[] _content;

			public string BaseAddress { get; }

			public FileServer(byte[] content)
			{
				_content = content;
				BaseAddress = $"http://127.0.0.1:{GetFreePort()}/";
				_listener.Prefixes.Add(BaseAddress);
				_listener.Start();
				Task.Run(AcceptLoop);
			}

			public void Dispose()
			{
				_listener.Close();
			}

			private async Task AcceptLoop()
			{
				while (true)
				{
					HttpListenerContext context;

					try
					{
						context = await _listener.GetContextAsync();
					}
					catch (Exception)
					{
						return;
					}

					try
					{
						var found = context.Request.Url.AbsolutePath == "/tables/v1.bin";
						var body = found ? _content : Encoding.UTF8.GetBytes("not here");
						context.Response.StatusCode = found ? 200 : 404;
						context.Response.ContentLength64 = body.Length;
						await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
						context.Response.Close();
					}
					catch (Exception)
					{
						// 클라이언트가 끊으면 무시한다
					}
				}
			}

			private static int GetFreePort()
			{
				var listener = new TcpListener(IPAddress.Loopback, 0);
				listener.Start();
				var port = ((IPEndPoint) listener.LocalEndpoint).Port;
				listener.Stop();
				return port;
			}
		}

		[UnityTest]
		public IEnumerator RealDownload_SavesAndLoadsOnTheThreadPool() => UniTask.ToCoroutine(async () =>
		{
			// 128KB 바이너리(0~255 반복)
			var content = new byte[128 * 1024];

			for (var i = 0; i < content.Length; i++)
			{
				content[i] = (byte) i;
			}

			string sha;

			using (var sha256 = SHA256.Create())
			{
				sha = BitConverter.ToString(sha256.ComputeHash(content)).Replace("-", string.Empty);
			}

			var root = Path.Combine(Path.GetTempPath(), "geuneda-resource-real-" + Guid.NewGuid().ToString("N"));

			using (var server = new FileServer(content))
			{
				try
				{
					var cache = new ResourceCache(new ResourceCacheOptions
					{
						HttpClient = new HttpClientBuilder().Build(),
						RootPath = root
					});
					var entry = new ResourceEntry("tables", "v1", content.Length, sha, server.BaseAddress + "tables/v1.bin");
					var mainThread = Thread.CurrentThread.ManagedThreadId;

					var downloaded = await cache.DownloadAsync(new[] { entry });
					Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId, "results come back to the calling thread");
					var loaded = await cache.LoadBytesAsync(entry);
					var check = await cache.CheckAsync(new[] { entry });

					Assert.IsTrue(downloaded.IsSuccess, downloaded.ToString());
					CollectionAssert.AreEqual(content, loaded.Value);
					Assert.IsTrue(check.Value.IsUpToDate);
					Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId);

					var missing = new ResourceEntry("tables", "v2", 1, sha, server.BaseAddress + "tables/v2.bin");
					var notFound = await cache.DownloadAsync(new[] { missing });
					Assert.AreEqual(HttpResultCodes.HttpError, notFound.Code);
					Assert.IsTrue((await cache.LoadBytesAsync(entry)).IsSuccess, "a failed download keeps the cached version");
				}
				finally
				{
					if (Directory.Exists(root))
					{
						Directory.Delete(root, true);
					}
				}
			}
		});
	}
}
