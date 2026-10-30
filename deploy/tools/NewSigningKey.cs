// Prints a new P-256 private key in PEM (PKCS#8) for signing access tokens.
// Used by ./deploy/dev.ps1 secrets: Windows PowerShell 5.1 runs on .NET Framework, which cannot
// export EC keys as PEM. Run with: dotnet run deploy/tools/NewSigningKey.cs
using System.Security.Cryptography;

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
Console.Write(key.ExportPkcs8PrivateKeyPem());
