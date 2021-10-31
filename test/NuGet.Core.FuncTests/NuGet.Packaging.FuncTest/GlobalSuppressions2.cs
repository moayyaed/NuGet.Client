// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Packaging.FuncTest.SigningTestFixture.CreateDefaultTrustedCertificateAuthorityAsync~System.Threading.Tasks.Task{Test.Utility.Signing.CertificateAuthority}")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Packaging.FuncTest.SigningTestFixture.CreateTrustedTestCertificateThatWillExpireSoon~Test.Utility.Signing.TrustedTestCert{Test.Utility.Signing.TestCertificate}")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Packaging.FuncTest.SigningTestFixture.CreateUntrustedTestCertificateThatWillExpireSoon~Test.Utility.Signing.TestCertificate")]
[assembly: SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Packaging.FuncTest.SigningTestFixture.Dispose")]
[assembly: SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "<Pending>", Scope = "member", Target = "~M:NuGet.Packaging.FuncTest.SigningTestFixture.Dispose")]
[assembly: SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "<Pending>", Scope = "type", Target = "~T:NuGet.Packaging.FuncTest.SigningTestFixture")]
