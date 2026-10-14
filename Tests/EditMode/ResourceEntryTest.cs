using System;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Resource.Tests
{
	[TestFixture]
	public class ResourceEntryTest
	{
		private const string Sha = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
		private const string Url = "https://cdn.test/tables/v1.json";

		[Test]
		public void Constructor_NormalizesHash_AndKeepsValues()
		{
			var entry = new ResourceEntry("tables", "2026.10_14-1", 4, Sha, Url);

			Assert.AreEqual("tables", entry.Category);
			Assert.AreEqual("2026.10_14-1", entry.Version);
			Assert.AreEqual(4, entry.Size);
			Assert.AreEqual(Sha.ToLowerInvariant(), entry.Sha256);
			Assert.AreEqual(new Uri(Url), entry.Url);
			Assert.AreEqual("tables@2026.10_14-1", entry.ToString());
		}

		[TestCase("console", "com10")]
		[TestCase("nullable", "v1.con")]
		[TestCase("aux-data", ".hidden")]
		public void Constructor_NamesThatOnlyLookLikeDeviceNames_AreAccepted(string category, string version)
		{
			Assert.DoesNotThrow(() => new ResourceEntry(category, version, 4, Sha, Url));
		}

		[TestCase("", "v1")]
		[TestCase("..", "v1")]
		[TestCase(".", "v1")]
		[TestCase("a/b", "v1")]
		[TestCase("a\\b", "v1")]
		[TestCase("tables", "../v1")]
		[TestCase("tables", "v 1")]
		[TestCase("tables", "0123456789012345678901234567890123456789012345678901234567890123x")]
		[TestCase("con", "v1")]
		[TestCase("tables", "nul")]
		[TestCase("tables", "com1.2")]
		[TestCase("lpt9", "v1")]
		[TestCase("tables", "v1.")]
		[TestCase("Tables", "v1")]
		[TestCase("tables", "V1")]
		[TestCase(null, "v1")]
		[TestCase("tables", null)]
		public void Constructor_UnsafeNames_Throw(string category, string version)
		{
			Assert.Throws<ArgumentException>(() => new ResourceEntry(category, version, 4, Sha, Url));
		}

		[TestCase(-1, Sha, Url)]
		[TestCase(4, "abc", Url)]
		[TestCase(4, null, Url)]
		[TestCase(4, Sha, "/tables/v1.json")]
		[TestCase(4, Sha, "ftp://cdn.test/v1")]
		[TestCase(4, Sha, "file:///etc/hosts")]
		[TestCase(4, Sha, null)]
		public void Constructor_InvalidSizeHashOrUrl_Throw(long size, string sha, string url)
		{
			Assert.Throws(Is.InstanceOf<ArgumentException>(), () => new ResourceEntry("tables", "v1", size, sha, url));
		}
	}
}
