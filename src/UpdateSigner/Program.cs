using System;
using System.IO;
using System.Security.Cryptography;

namespace Ruinarch.Modding.UpdateSigner
{
	/// <summary>
	/// Signs in-game update manifests. The private key never leaves the release owner's
	/// machine; the public key is compiled into the mod menu (Updater.PublicKey).
	///   keygen &lt;private-key.xml&gt;          new 3072-bit key pair; prints the public key
	///   sign &lt;private-key.xml&gt; &lt;file&gt;      writes &lt;file&gt;.sig (RSA PKCS#1 v1.5, SHA-256)
	///   verify &lt;public-key.xml&gt; &lt;file&gt;    checks &lt;file&gt;.sig
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			try
			{
				switch (args.Length > 0 ? args[0] : "")
				{
					case "keygen" when args.Length == 2:
						if (File.Exists(args[1])) throw new IOException(args[1] + " already exists; refusing to overwrite a signing key.");
						using (var rsa = RSA.Create(3072))
						{
							Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
							File.WriteAllText(args[1], rsa.ToXmlString(true));
							if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(args[1], UnixFileMode.UserRead | UnixFileMode.UserWrite);
							Console.WriteLine(rsa.ToXmlString(false));
						}
						return 0;
					case "sign" when args.Length == 3:
						using (var rsa = RSA.Create())
						{
							rsa.FromXmlString(File.ReadAllText(args[1]));
							File.WriteAllBytes(args[2] + ".sig", rsa.SignData(File.ReadAllBytes(args[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
						}
						Console.WriteLine("signed " + args[2]);
						return 0;
					case "verify" when args.Length == 3:
						using (var rsa = RSA.Create())
						{
							rsa.FromXmlString(File.ReadAllText(args[1]));
							bool ok = rsa.VerifyData(File.ReadAllBytes(args[2]), File.ReadAllBytes(args[2] + ".sig"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
							Console.WriteLine(ok ? "valid" : "INVALID");
							return ok ? 0 : 1;
						}
					default:
						Console.Error.WriteLine("usage: keygen <private-key.xml> | sign <private-key.xml> <file> | verify <public-key.xml> <file>");
						return 2;
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine("ERROR: " + e.Message);
				return 1;
			}
		}
	}
}
