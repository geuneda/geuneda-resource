using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	/// <summary>주소별 파일을 돌려주는 가짜 HTTP 전송입니다. 게이트가 없으면 요청이 동기적으로 끝납니다.</summary>
	internal sealed class FakeFileServer : IHttpTransport
	{
		public const string BaseAddress = "https://cdn.test/";

		public readonly List<HttpRequest> Requests = new List<HttpRequest>();
		public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
		public readonly Dictionary<string, int> Statuses = new Dictionary<string, int>();
		public readonly Dictionary<string, Result<HttpResponse>> Failures = new Dictionary<string, Result<HttpResponse>>();

		/// <summary>주소별로 두면 그 요청이 게이트가 끝날 때까지 기다립니다.</summary>
		public readonly Dictionary<string, UniTaskCompletionSource> Gates = new Dictionary<string, UniTaskCompletionSource>();

		/// <summary>이 주소의 요청은 예외를 던집니다(오용·구현 버그 흉내).</summary>
		public readonly HashSet<string> Throws = new HashSet<string>();

		/// <summary>true 면 게이트에서 기다리는 요청이 취소를 무시합니다.</summary>
		public bool IgnoreCancellation;

		/// <summary>true 면 게이트에서 기다리는 요청이 예외를 던지는 취소 콜백을 등록합니다.</summary>
		public bool ThrowOnCancel;

		public int InFlight;
		public int MaxInFlight;

		public HttpClient CreateClient()
		{
			return new HttpClientBuilder().WithTransport(this).Build();
		}

		/// <summary>내용을 올리고 그 파일을 가리키는 항목을 돌려줍니다.</summary>
		public ResourceEntry Publish(string category, string version, string content)
		{
			var bytes = Encoding.UTF8.GetBytes(content);
			var url = $"{BaseAddress}{category}/{version}.json";
			Files[url] = bytes;
			return new ResourceEntry(category, version, bytes.Length, Sha256(bytes), url);
		}

		public static string Sha256(byte[] bytes)
		{
			using (var sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
			}
		}

		public int Count(ResourceEntry entry)
		{
			return Requests.FindAll(request => request.Uri == entry.Url).Count;
		}

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			var url = request.Uri.AbsoluteUri;
			Requests.Add(request);
			InFlight++;
			MaxInFlight = Math.Max(MaxInFlight, InFlight);

			if (Throws.Contains(url))
			{
				InFlight--;
				throw new ArgumentException($"broken request {url}");
			}

			try
			{
				if (Gates.TryGetValue(url, out var gate))
				{
					using (ThrowOnCancel ? cancellationToken.Register(() => throw new InvalidOperationException("cancel callback")) : default(CancellationTokenRegistration))
					{
						await (IgnoreCancellation ? gate.Task : gate.Task.AttachExternalCancellation(cancellationToken));
					}
				}
			}
			catch (OperationCanceledException)
			{
				InFlight--;
				return Result<HttpResponse>.Fail(ResultCodes.Cancelled, "cancelled");
			}

			InFlight--;

			if (Failures.TryGetValue(url, out var failure))
			{
				return failure;
			}

			var status = Statuses.TryGetValue(url, out var configured) ? configured : Files.ContainsKey(url) ? 200 : 404;
			var body = Files.TryGetValue(url, out var file) ? file : Array.Empty<byte>();
			return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, status, body, new Dictionary<string, string>()));
		}
	}
}
