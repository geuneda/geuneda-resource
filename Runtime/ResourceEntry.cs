using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource
{
	/// <summary>
	/// 리소스 매니페스트 항목 하나입니다. 바꾸지 않는 객체입니다. 받은 파일은 <see cref="Size"/> 와 <see cref="Sha256"/> 로 검증하므로,
	/// 항목은 서명된 문서(com.geuneda.appsetting 의 <c>resources</c> 노드 등) 처럼 믿을 수 있는 곳에서 가져옵니다.
	/// </summary>
	public sealed class ResourceEntry
	{
		private const int MaxNameLength = 64;

		// Windows 장치 이름은 확장자를 붙여도(con.1) 파일 이름으로 쓸 수 없다
		private static readonly string[] ReservedNames =
		{
			"con", "prn", "aux", "nul",
			"com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
			"lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
		};

		/// <summary>종류 이름입니다(예: <c>tables</c>). 카테고리마다 한 버전만 캐시에 남습니다.</summary>
		public string Category { get; }

		/// <summary>파일 버전입니다. 캐시 파일 이름(<c>&lt;루트&gt;/&lt;카테고리&gt;/&lt;버전&gt;.&lt;해시 앞 16자&gt;.bin</c>)이 됩니다.</summary>
		public string Version { get; }

		/// <summary>파일 크기(바이트)입니다.</summary>
		public long Size { get; }

		/// <summary>파일 SHA-256(소문자 16진수 64자)입니다.</summary>
		public string Sha256 { get; }

		/// <summary>파일 주소(http·https 절대 주소)입니다.</summary>
		public Uri Url { get; }

		/// <param name="category">
		/// 카테고리. <c>[a-z0-9._-]</c> 1~64자. <c>.</c>·<c>..</c>, 끝의 점, Windows 장치 이름(<c>con</c>, <c>nul</c>, <c>com1</c> 등, 뒤에 점이 붙어도)은 안 됩니다.
		/// 대소문자를 가리지 않는 파일 시스템에서 다른 카테고리가 같은 폴더가 되지 않게 소문자만 받습니다
		/// </param>
		/// <param name="version">버전. 카테고리와 같은 규칙</param>
		/// <param name="size">크기(0 이상)</param>
		/// <param name="sha256">SHA-256 16진수 64자(대소문자 무시)</param>
		/// <param name="url">호스트가 있는 http·https 절대 주소</param>
		/// <exception cref="ArgumentException">값이 규칙에 맞지 않는 경우(파일 이름이 경로를 벗어나지 못하게 이름을 제한함)</exception>
		public ResourceEntry(string category, string version, long size, string sha256, string url)
		{
			if (!IsValidName(category) || !IsValidName(version))
			{
				throw new ArgumentException($"Category and version need 1-{MaxNameLength} characters from a-z 0-9 . _ - (lowercase only, no trailing '.', not . .. or Windows device names such as con): '{category}', '{version}'");
			}

			if (size < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(size), size, "Size must not be negative");
			}

			Sha256 = NormalizeSha256(sha256) ?? throw new ArgumentException($"SHA-256 must be 64 hex characters: '{sha256}'", nameof(sha256));

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(uri.Host))
			{
				throw new ArgumentException($"Url must be an absolute http or https address: '{url}'", nameof(url));
			}

			Category = category;
			Version = version;
			Size = size;
			Url = uri;
		}

		public override string ToString()
		{
			return $"{Category}@{Version}";
		}

		/// <summary>카테고리·버전·크기·해시가 같은 항목인지 봅니다(주소는 보지 않음).</summary>
		internal bool IsSameFile(ResourceEntry other)
		{
			return Category == other.Category && Version == other.Version && Size == other.Size && Sha256 == other.Sha256;
		}

		private static bool IsValidName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name == "." || name == "..")
			{
				return false;
			}

			foreach (var c in name)
			{
				if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
				{
					return false;
				}
			}

			// 끝의 점은 Windows 가 지워 다른 이름이 된다. 첫 점 앞이 장치 이름이면 예약 이름이다
			if (name[name.Length - 1] == '.')
			{
				return false;
			}

			var dot = name.IndexOf('.');
			return Array.IndexOf(ReservedNames, dot < 0 ? name : name.Substring(0, dot)) < 0;
		}

		private static string NormalizeSha256(string value)
		{
			if (value == null || value.Length != 64)
			{
				return null;
			}

			foreach (var c in value)
			{
				if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
				{
					return null;
				}
			}

			return value.ToLowerInvariant();
		}
	}
}
