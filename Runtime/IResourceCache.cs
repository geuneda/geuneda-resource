using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>
	/// 원격 리소스 캐시입니다. 기본 구현은 <see cref="ResourceCache"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 캐시 파일(<c>&lt;루트&gt;/&lt;카테고리&gt;/&lt;버전&gt;.&lt;SHA-256 앞 16자&gt;.bin</c>)이 맞는지는 버전 비교가 아니라 항목의 크기와 SHA-256 으로 판단합니다.
	/// 받은 파일은 검증한 뒤에만 저장하고(임시 파일에 쓴 뒤 이름 바꾸기), 저장이 끝난 뒤에 그 카테고리의 다른 파일을 지웁니다.
	/// 조회(<see cref="CheckAsync"/>, <see cref="LoadBytesAsync"/>, <see cref="GetFilePathAsync"/>)는 파일을 지우지 않습니다. 맞지 않는 파일은 다음 저장이 교체·정리합니다.
	/// </para>
	/// <para>메인 스레드에서 부릅니다. 파일 입출력과 해시 계산은 스레드 풀에서 하고 결과와 진행률 콜백은 메인 스레드로 돌아옵니다.</para>
	/// <para>한 호출에 넘기는 항목은 카테고리마다 하나여야 합니다(같은 카테고리의 다른 버전은 <see cref="ArgumentException"/>).</para>
	/// </remarks>
	public interface IResourceCache
	{
		/// <summary>
		/// 캐시에 맞는 파일이 없는 항목과 총 크기를 돌려줍니다. 받기 전 확인 창(용량 안내)에 씁니다. 파일을 읽어 해시를 계산하므로 큰 파일은 시간이 걸립니다.
		/// </summary>
		/// <exception cref="ArgumentNullException">항목 목록이나 항목이 null 인 경우</exception>
		UniTask<Result<ResourceCheck>> CheckAsync(IEnumerable<ResourceEntry> entries, CancellationToken cancellationToken = default);

		/// <summary>
		/// 캐시에 맞는 파일이 없는 항목만 받아 저장합니다. 이미 맞는 항목은 요청하지 않습니다. 동시에 받는 수는 <see cref="ResourceCacheOptions.MaxConcurrentDownloads"/> 까지입니다.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 하나가 실패해도 나머지는 계속 받고, 끝난 뒤 첫 실패의 코드와 실패한 항목 목록을 돌려줍니다(성공한 파일은 저장된 채로 남음).
		/// 취소하면 아직 시작하지 않은 항목은 받지 않고 진행 중인 요청도 멈춥니다(<c>Cancelled</c>).
		/// </para>
		/// <para>다른 <see cref="DownloadAsync"/> 가 진행 중이면 기다리지 않고 <c>ResultCodes.InProgress</c> 입니다.</para>
		/// </remarks>
		/// <param name="progress">파일 하나를 저장할 때마다(처음에 0 으로 한 번) 메인 스레드에서 부릅니다. 예외는 로그만 남깁니다</param>
		/// <exception cref="ArgumentNullException">항목 목록이나 항목이 null 인 경우</exception>
		UniTask<Result> DownloadAsync(IEnumerable<ResourceEntry> entries, Action<ResourceDownloadProgress> progress = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// 캐시 파일을 읽어 크기·SHA-256 을 확인한 뒤 바이트를 돌려줍니다. 없거나 맞지 않으면 <see cref="ResourceResultCodes.NotCached"/> 입니다(파일은 그대로 두고 다음 저장이 교체).
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="entry"/> 가 null 인 경우</exception>
		UniTask<Result<byte[]>> LoadBytesAsync(ResourceEntry entry, CancellationToken cancellationToken = default);

		/// <summary>
		/// 캐시 파일이 맞는지 확인한 뒤 경로를 돌려줍니다(파일로 여는 API 용). 다음 다운로드가 새 버전을 저장하면 이 파일은 지워질 수 있습니다.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="entry"/> 가 null 인 경우</exception>
		UniTask<Result<string>> GetFilePathAsync(ResourceEntry entry, CancellationToken cancellationToken = default);
	}
}
