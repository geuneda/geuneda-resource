using System;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary><see cref="ResourceCache"/> 설정입니다.</summary>
	public sealed class ResourceCacheOptions
	{
		/// <summary>
		/// 파일을 받을 HTTP 클라이언트입니다. 필수입니다. 일시 실패(연결, 제한 시간, 5xx) 재시도는 <c>RetryMiddleware</c> 를 넣어 맡깁니다(GET 이라 재시도해도 됨).
		/// 응답 본문을 메모리에 받으므로 테이블 같은 데이터 파일용입니다.
		/// </summary>
		public IHttpClient HttpClient { get; set; }

		/// <summary>캐시 폴더입니다. null 이면 <c>Application.persistentDataPath/geuneda-resources</c> 입니다.</summary>
		public string RootPath { get; set; }

		/// <summary>동시에 받는 파일 수입니다(1 이상). 기본 2입니다.</summary>
		public int MaxConcurrentDownloads { get; set; } = 2;

		/// <summary>파일 요청 하나의 제한 시간입니다. 넘으면 <c>ResultCodes.Timeout</c> 입니다. 기본 60초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
	}
}
