using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>
	/// 캐시 파일 입출력입니다. 스레드 풀에서 부르므로 Unity API 를 쓰지 않습니다. 이름은 <see cref="ResourceEntry"/> 가 확인해 경로를 벗어나지 않습니다.
	/// </summary>
	/// <remarks>
	/// 파일 이름은 <c>&lt;버전&gt;.&lt;SHA-256 앞 16자&gt;.bin</c> 입니다. 같은 버전이라도 내용이 다르면 다른 파일이라, 새 파일을 다 놓은 뒤에야 이전 파일을 지웁니다.
	/// 파일을 지우는 곳은 <see cref="Commit"/> 하나뿐입니다(조회는 지우지 않음).
	/// 대소문자를 가리지 않거나 장치 이름을 예약한 파일 시스템(Windows)에서도 같은 파일이 되지 않게 이름 규칙은 <see cref="ResourceEntry"/> 가 정합니다.
	/// </remarks>
	internal static class ResourceFiles
	{
		private const string Extension = ".bin";
		private const string TemporaryExtension = ".tmp";
		private const int HashPrefixLength = 16;

		public static string PathOf(string root, ResourceEntry entry)
		{
			// 앞 16자(64비트)로 충분하다. 우연히 같아도 읽을 때 전체 해시를 확인해 맞지 않는 파일로 다룬다
			return Path.Combine(root, entry.Category, $"{entry.Version}.{entry.Sha256.Substring(0, HashPrefixLength)}{Extension}");
		}

		/// <summary>파일이 있고 크기·SHA-256 이 맞으면 true 입니다. 크기가 다르면 해시를 계산하지 않습니다.</summary>
		public static bool IsValid(string path, ResourceEntry entry)
		{
			var file = new FileInfo(path);

			if (!file.Exists || file.Length != entry.Size)
			{
				return false;
			}

			using (var stream = file.OpenRead())
			{
				return Sha256Hex(stream) == entry.Sha256;
			}
		}

		/// <summary>파일 내용입니다. 없으면 null 입니다.</summary>
		public static byte[] ReadIfExists(string path)
		{
			try
			{
				return File.ReadAllBytes(path);
			}
			catch (FileNotFoundException)
			{
				return null;
			}
			catch (DirectoryNotFoundException)
			{
				return null;
			}
		}

		public static bool Matches(byte[] bytes, ResourceEntry entry)
		{
			return Mismatch(bytes, entry) == null;
		}

		/// <summary>크기·SHA-256 이 맞으면 null, 아니면 무엇이 다른지 설명입니다.</summary>
		public static string Mismatch(byte[] bytes, ResourceEntry entry)
		{
			if (bytes.LongLength != entry.Size)
			{
				return $"size {bytes.LongLength} is not {entry.Size}";
			}

			var sha256 = Sha256Hex(bytes);
			return sha256 == entry.Sha256 ? null : $"SHA-256 {sha256} is not {entry.Sha256}";
		}

		private static void Delete(string path)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		/// <summary>
		/// 검증한 바이트를 같은 폴더의 임시 파일에 쓰고 디스크로 내보낸 뒤 이름을 바꿉니다. 같은 이름의 정상 파일이 이미 있으면 쓰지 않고 그것을 씁니다.
		/// 그 뒤 같은 카테고리의 다른 파일(다른 버전·내용, 맞지 않는 파일)과 남은 임시 파일을 지웁니다.
		/// 지우지 못한 파일(Windows 에서 열려 있는 파일 등)은 다음 저장 때 다시 지우고, 그 경로를 돌려줍니다.
		/// </summary>
		/// <exception cref="IOException">같은 이름 파일을 다시 확인하지 못했거나(그 파일은 그대로 둠), 쓰거나 이름을 바꾸지 못한 경우(임시 파일은 지움)</exception>
		/// <exception cref="UnauthorizedAccessException">권한이 없는 경우</exception>
		public static IReadOnlyList<string> Commit(string root, ResourceEntry entry, byte[] bytes)
		{
			var path = PathOf(root, entry);
			var directory = Path.GetDirectoryName(path);
			Directory.CreateDirectory(directory);

			// 같은 이름(같은 버전·해시 앞부분)이 있으면 다시 확인한다. 판정 때 읽지 못했을 뿐인 정상 파일이면 그대로 쓰고,
			// 다시 확인하다 입출력 오류가 나면 예외로 끝나 기존 파일은 그대로 남는다. 맞지 않는 파일만 지운 뒤 이름을 바꾼다(잃을 내용이 없음)
			if (!File.Exists(path) || !IsValid(path, entry))
			{
				var temporary = Path.Combine(directory, $".{entry.Version}.{Guid.NewGuid():N}{TemporaryExtension}");

				try
				{
					using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
						stream.Write(bytes, 0, bytes.Length);
						stream.Flush(true);
					}

					Delete(path);
					File.Move(temporary, path);
				}
				finally
				{
					TryDelete(temporary);
				}
			}

			var leftovers = new List<string>();
			var kept = Path.GetFileName(path);

			foreach (var file in Directory.GetFiles(directory))
			{
				// 경로 문자열 형태(구분자 중복 등)와 관계없이 이름으로 비교한다
				var name = Path.GetFileName(file);
				var stale = name.EndsWith(TemporaryExtension, StringComparison.Ordinal) && name.StartsWith(".", StringComparison.Ordinal)
					|| name.EndsWith(Extension, StringComparison.Ordinal) && !string.Equals(name, kept, StringComparison.Ordinal);

				if (stale && !TryDelete(file))
				{
					leftovers.Add(file);
				}
			}

			return leftovers;
		}

		public static string Sha256Hex(byte[] bytes)
		{
			using (var sha = SHA256.Create())
			{
				return ToHex(sha.ComputeHash(bytes));
			}
		}

		private static string Sha256Hex(Stream stream)
		{
			using (var sha = SHA256.Create())
			{
				return ToHex(sha.ComputeHash(stream));
			}
		}

		private static bool TryDelete(string path)
		{
			try
			{
				Delete(path);
				return true;
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static string ToHex(byte[] bytes)
		{
			const string digits = "0123456789abcdef";
			var chars = new char[bytes.Length * 2];

			for (var i = 0; i < bytes.Length; i++)
			{
				chars[i * 2] = digits[bytes[i] >> 4];
				chars[i * 2 + 1] = digits[bytes[i] & 0xF];
			}

			return new string(chars);
		}
	}
}
