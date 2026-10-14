// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>다운로드 진행률입니다. 파일 하나를 받아 저장할 때마다 옵니다(처음에 0 으로 한 번).</summary>
	public readonly struct ResourceDownloadProgress
	{
		public int CompletedFiles { get; }
		public int TotalFiles { get; }
		public long CompletedBytes { get; }
		public long TotalBytes { get; }

		public ResourceDownloadProgress(int completedFiles, int totalFiles, long completedBytes, long totalBytes)
		{
			CompletedFiles = completedFiles;
			TotalFiles = totalFiles;
			CompletedBytes = completedBytes;
			TotalBytes = totalBytes;
		}

		public override string ToString()
		{
			return $"{CompletedFiles}/{TotalFiles} files, {CompletedBytes}/{TotalBytes} bytes";
		}
	}
}
