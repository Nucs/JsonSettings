using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Asserts that the shipped assemblies really are strong-named with the key in Open.snk.
    ///
    ///     WHY A TEST AND NOT JUST THE BUILD SETTING. Strong naming fails silently. Turn signing off
    ///     and the file names are unchanged, every other test still passes, `dotnet pack` still
    ///     succeeds, and the only symptom is `PublicKeyToken=null` in an identity string that nothing
    ///     prints. NumSharp shipped unsigned for seven years exactly that way - its SignAssembly sat
    ///     in a build configuration CI never used - and no test noticed, because it had none.
    ///
    ///     WHAT THIS ADDS OVER .github/check-strong-name.ps1. That script inspects PE files on disk
    ///     and is the gate that blocks a release; it is the stricter of the two and it is what proves
    ///     the signature is real rather than delay- or public-signed. This runs in-process against
    ///     the assemblies actually loaded by the test host, so it is what proves the identity is
    ///     usable at RUNTIME - that friend access resolves and that the token a consumer would bind
    ///     against is the expected one. Both are cheap; neither subsumes the other.
    ///
    ///     THE LITERALS BELOW ARE A DELIBERATE SECOND COPY. Directory.Build.props derives what it
    ///     signs with from Open.snk; this file states independently what the answer must be. A test
    ///     that read the key from the same place the build reads it would agree with any key,
    ///     including a replaced one, and would assert nothing. check-strong-name.ps1 ties Open.snk to
    ///     the $(PublicKey) literal; this ties the built output to the expected identity; between
    ///     them there is no pair that can drift unnoticed.
    /// </summary>
    [TestClass]
    public class StrongNameTests {
        /// <summary>Microsoft's published open-source key. See Directory.Build.props for what it is and is not.</summary>
        private const string ExpectedPublicKey =
            "00240000048000009400000006020000002400005253413100040000010001004b86c4cb78549b34bab61a3b1800e23b" +
            "feb5b3ec390074041536a7e3cbd97f5f04cf0f857155a8928eaa29ebfd11cfbbad3ba70efea7bda3226c6a8d370a4cd3" +
            "03f714486b6ebc225985a638471e6ef571cc92a4613c00b8fa65d61ccee0cbe5f36330c9a01f4183559f1bef24cc2917" +
            "c6d913e3a541333a1d05d9bed22b38cb";

        private const string ExpectedPublicKeyToken = "cc7b13ffcd2ddd51";

        private static Assembly CoreAssembly => typeof(JsonSettings).Assembly;

        // Referenced through an INTERNAL type on purpose. If the keyed InternalsVisibleTo in
        // JsonSettings.Autosave.csproj stopped resolving, this file would not compile - which makes
        // the whole test class a build-time assertion before it is a runtime one.
        //
        // global:: is required, not stylistic: this namespace is Nucs.JsonSettings.Tests, and the
        // test project has its own Nucs.JsonSettings.Tests.Autosave folder, which an unqualified
        // `Autosave.` binds to first.
        private static Assembly AutosaveAssembly => typeof(global::Nucs.JsonSettings.Autosave.TypeValidation).Assembly;

        private static IEnumerable<Assembly> ShippedAssemblies {
            get {
                yield return CoreAssembly;
                yield return AutosaveAssembly;
            }
        }

        [TestMethod]
        public void ShippedAssemblies_CarryTheExpectedPublicKeyToken() {
            foreach (var assembly in ShippedAssemblies) {
                var token = assembly.GetName().GetPublicKeyToken();

                token.Should().NotBeNull($"{assembly.GetName().Name} must be strong-named");
                token!.Length.Should().Be(8, $"{assembly.GetName().Name} carries a public key token of the wrong size");

                ToHex(token).Should().Be(ExpectedPublicKeyToken,
                    $"{assembly.GetName().Name} must be signed with Open.snk; a null or different token means " +
                    "SignAssembly or AssemblyOriginatorKeyFile in Directory.Build.props stopped taking effect");
            }
        }

        [TestMethod]
        public void ShippedAssemblies_CarryTheFullExpectedPublicKey() {
            // The token is a truncated hash of the key, so checking it alone leaves the key itself
            // unverified. Consumers write the full key in their own InternalsVisibleTo declarations.
            foreach (var assembly in ShippedAssemblies) {
                var key = assembly.GetName().GetPublicKey();

                key.Should().NotBeNull($"{assembly.GetName().Name} must carry a full public key, not only a token");
                ToHex(key!).Should().Be(ExpectedPublicKey, $"{assembly.GetName().Name} is signed with an unexpected key");
            }
        }

        [TestMethod]
        public void TestAssembly_IsSignedWithTheSameKey() {
            // Not incidental. A friend reference names an assembly BY public key, so an unsigned test
            // assembly would fail to match the declarations below no matter how they were written -
            // and the failure would surface as "inaccessible due to its protection level" on a member
            // that is plainly there, which sends the reader looking anywhere but at signing.
            var token = typeof(StrongNameTests).Assembly.GetName().GetPublicKeyToken();

            token.Should().NotBeNull("the test assembly must be signed for InternalsVisibleTo to resolve");
            ToHex(token!).Should().Be(ExpectedPublicKeyToken);
        }

        [TestMethod]
        public void FriendDeclarations_AreAllKeyed() {
            // A keyless InternalsVisibleTo in a signed assembly is CS1726, so this cannot be violated
            // by hand-writing the attribute. It CAN be violated by $(PublicKey) going missing from
            // Directory.Build.props: the SDK then generates the attribute with no key at all and the
            // build fails - unless GenerateAssemblyInfo is off, or someone re-adds the declarations as
            // source. This states the requirement rather than relying on that chain.
            foreach (var assembly in ShippedAssemblies) {
                var declarations = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                    .Select(attribute => attribute.AssemblyName)
                    .ToArray();

                declarations.Should().NotBeEmpty($"{assembly.GetName().Name} is expected to grant friend access");

                foreach (var declaration in declarations) {
                    declaration.Should().Contain("PublicKey=",
                        $"friend declaration '{declaration}' in {assembly.GetName().Name} is keyless and would match no assembly");

                    var key = declaration.Substring(declaration.IndexOf("PublicKey=", StringComparison.Ordinal) + "PublicKey=".Length).Trim();
                    key.ToLowerInvariant().Should().Be(ExpectedPublicKey,
                        $"friend declaration '{declaration}' names a key that is not ours, so it grants access to nobody");
                }
            }
        }

        [TestMethod]
        public void FriendAccess_ResolvesAtRuntime() {
            // The compiler already proved this by binding the two internal types below. This adds the
            // half the compiler cannot check: that the friend declarations name THESE assemblies, so
            // the access survives at runtime rather than only satisfying the reference assembly.
            var testAssemblyName = typeof(StrongNameTests).Assembly.GetName().Name;

            foreach (var assembly in ShippedAssemblies) {
                assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                    .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
                    .Should().Contain(testAssemblyName,
                        $"{assembly.GetName().Name} must befriend the test assembly by that exact name");
            }

            typeof(global::Nucs.JsonSettings.Inline.Paths).IsVisible.Should()
                .BeFalse("Paths is internal to JsonSettings; reaching it here is friend access, not a public API");
            typeof(global::Nucs.JsonSettings.Autosave.TypeValidation).IsVisible.Should()
                .BeFalse("TypeValidation is internal to JsonSettings.Autosave");
        }

        private static string ToHex(byte[] bytes) {
            // BitConverter rather than Convert.ToHexString: this compiles for net472 and netstandard2.0.
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
