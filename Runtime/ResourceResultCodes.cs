// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>
	/// resource 패키지의 결과 코드입니다(core 결과 코드 범위 표의 5000 블록).
	/// </summary>
	/// <remarks>
	/// 연결 실패, 제한 시간, 취소, 다른 다운로드 진행 중은 core 공용 코드(<c>NetworkError</c>, <c>Timeout</c>, <c>Cancelled</c>, <c>InProgress</c>)를,
	/// 2xx 가 아닌 응답은 http 의 <c>HttpResultCodes.HttpError</c> 를 씁니다.
	/// </remarks>
	public static class ResourceResultCodes
	{
		/// <summary>이 패키지 코드 블록의 시작입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeStart = 5000;

		/// <summary>캐시에 맞는 파일이 없습니다(없거나 크기·해시가 다름. 맞지 않는 파일은 다음 저장이 교체). 받은 뒤 다시 읽습니다.</summary>
		public const int NotCached = RangeStart + 1;

		/// <summary>받은 파일의 크기나 SHA-256 이 항목과 다릅니다. 저장하지 않았습니다.</summary>
		public const int IntegrityFailed = RangeStart + 2;

		/// <summary>캐시 폴더에 쓰거나 지우지 못했습니다(공간 부족, 권한).</summary>
		public const int StorageFailed = RangeStart + 3;
	}
}
