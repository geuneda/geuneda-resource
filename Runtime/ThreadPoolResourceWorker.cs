using System;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>스레드 풀에서 돌리고 메인 스레드로 돌아옵니다.</summary>
	internal sealed class ThreadPoolResourceWorker : IResourceWorker
	{
		public UniTask<T> Run<T>(Func<T> work)
		{
			return UniTask.RunOnThreadPool(work);
		}
	}
}
