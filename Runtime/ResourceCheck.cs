using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary><see cref="IResourceCache.CheckAsync"/> 결과입니다. 받아야 할 항목과 총 크기입니다(받기 전 확인 창에 씀).</summary>
	public sealed class ResourceCheck
	{
		/// <summary>캐시에 맞는 파일이 없는 항목입니다(넘긴 순서).</summary>
		public IReadOnlyList<ResourceEntry> Missing { get; }

		/// <summary><see cref="Missing"/> 크기의 합(바이트)입니다.</summary>
		public long TotalBytes { get; }

		public bool IsUpToDate => Missing.Count == 0;

		internal ResourceCheck(IReadOnlyList<ResourceEntry> missing, long totalBytes)
		{
			Missing = missing;
			TotalBytes = totalBytes;
		}
	}
}
