using System;
using System.IO;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	/// <summary>처음 몇 번의 파일 작업을 입출력 오류(잠긴 파일 등)로 실패시키고, 그 뒤로는 같은 스레드에서 바로 돌립니다.</summary>
	internal sealed class FailingResourceWorker : IResourceWorker
	{
		private readonly InlineResourceWorker _inner = new InlineResourceWorker();

		public int FailuresLeft;

		public UniTask<T> Run<T>(Func<T> work)
		{
			if (FailuresLeft > 0)
			{
				FailuresLeft--;
				return UniTask.FromException<T>(new IOException("file is locked"));
			}

			return _inner.Run(work);
		}
	}
}
