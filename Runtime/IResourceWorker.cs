using System;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>파일 입출력과 해시 계산을 돌리는 곳입니다. 기본은 스레드 풀이고, 테스트는 같은 스레드에서 바로 돌려 결과를 동기적으로 받습니다.</summary>
	internal interface IResourceWorker
	{
		/// <summary><paramref name="work"/> 를 돌리고 호출한 스레드(메인 스레드)로 돌아옵니다. 예외는 그대로 전달합니다.</summary>
		UniTask<T> Run<T>(Func<T> work);
	}
}
