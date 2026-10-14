using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	/// <summary>
	/// 파일 작업을 쌓아 두고 테스트가 정한 순서로 돌립니다. 경합 순서(조회의 후속 작업이 저장 뒤에 도는 경우 등)를 만들 때 씁니다.
	/// 작업을 돌리면 기다리던 코드가 그 자리에서 이어서 돕니다.
	/// </summary>
	internal sealed class DeferredResourceWorker : IResourceWorker
	{
		private readonly List<Action> _pending = new List<Action>();

		public int PendingCount => _pending.Count;

		public UniTask<T> Run<T>(Func<T> work)
		{
			var completion = new UniTaskCompletionSource<T>();

			_pending.Add(() =>
			{
				T result;

				try
				{
					result = work();
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
					return;
				}

				completion.TrySetResult(result);
			});

			return completion.Task;
		}

		/// <summary>가장 먼저 쌓인 작업 하나를 돌립니다.</summary>
		public void RunOldest()
		{
			var next = _pending[0];
			_pending.RemoveAt(0);
			next();
		}

		/// <summary>쌓인 작업이 없어질 때까지 가장 나중에 쌓인 것부터 돌립니다(새로 쌓이는 작업 포함).</summary>
		public void RunAllNewestFirst()
		{
			while (_pending.Count > 0)
			{
				var next = _pending[_pending.Count - 1];
				_pending.RemoveAt(_pending.Count - 1);
				next();
			}
		}
	}
}
