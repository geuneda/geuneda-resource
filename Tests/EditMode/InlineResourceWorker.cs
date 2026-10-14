using System;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	/// <summary>같은 스레드에서 바로 돌려 테스트가 동기적으로 결과를 받습니다.</summary>
	internal sealed class InlineResourceWorker : IResourceWorker
	{
		public int Runs;

		public UniTask<T> Run<T>(Func<T> work)
		{
			Runs++;

			try
			{
				return UniTask.FromResult(work());
			}
			catch (Exception exception)
			{
				return UniTask.FromException<T>(exception);
			}
		}
	}
}
