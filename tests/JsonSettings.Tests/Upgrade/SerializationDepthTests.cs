using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Upgrade {
    /// <summary>
    ///     Covers how deeply a settings object may be nested before it stops loading.
    /// </summary>
    /// <remarks>
    ///     THIS ONE IS NOT A PURE 2.0.x BASELINE. The other files in this folder assert the exact
    ///     behaviour of 2.0.1/2.0.2 and the fixes restore it verbatim. Depth is different, and
    ///     deliberately so:
    ///
    ///       2.0.x           MaxDepth was null - unlimited nesting on load.
    ///       2.1.0           inherited Json.NET 13's new default of 64 when Newtonsoft.Json was
    ///                       upgraded 12 -> 13. SerializationSettings never set MaxDepth, so the
    ///                       library silently took the dependency's default. Saving stayed
    ///                       unlimited, so an app could write a file it then failed to read back.
    ///       this build      MaxDepth is set explicitly to 128 - well past any realistic settings
    ///                       graph, and low enough to remain a WORKING backstop.
    ///
    ///     WHY 128 AND NOT MORE. This reader is recursive and, with TypeNameHandling.Auto on, it
    ///     exhausts the stack at roughly 0.42 levels per KB - measured through the real load path at
    ///     depth ~110 on a 256KB thread, ~230 on 512KB, ~430 on a 1MB thread. A limit turns that
    ///     uncatchable StackOverflow into a catchable <see cref="JsonSettingsException"/> only if it
    ///     fires BELOW the overflow depth. 512 sits above it on every ordinary stack and so would
    ///     never run; 128 clears any real settings graph yet still fires first on a 512KB-or-larger
    ///     thread. See the note on SerializationSettings in JsonSettings.cs.
    ///
    ///     WHY THESE TESTS PIN THEIR OWN STACK. Because the limit's usefulness is a race between it
    ///     and the stack, the depths here would be at the mercy of whatever stack the test runner
    ///     hands each thread - VSTest's is small enough (~256KB) that reading 128 levels overflows
    ///     before the 128 limit can fire, which would crash the host rather than fail a test. That is
    ///     a property of the runner, not of the library. Every load and save below therefore runs on
    ///     a thread with a large, fixed stack via <see cref="OnDeepStack"/>, so what is under test is
    ///     the library's limit logic - does the bound fire, does null lift it, is the default what it
    ///     claims - independent of the host's stack. The stack-vs-depth relationship itself is
    ///     documented and reproduced in docs/UPGRADE-2.0.x-to-2.1.0.md rather than asserted here,
    ///     because its numbers are environment-specific.
    ///
    ///     WHY NO TEST SAVES A PATHOLOGICALLY DEEP GRAPH. Serialization is unbounded recursion -
    ///     MaxDepth guards reading, not writing - so every beyond-the-bound input is built as JSON
    ///     text and loaded; nothing writes one.
    /// </remarks>
    [TestClass]
    public class SerializationDepthTests {
        /// <summary>
        ///     The depth limit this build ships. Stated once here so every row reads against a name.
        /// </summary>
        private const int DefaultMaxDepth = 128;

        /// <summary>
        ///     A depth past <see cref="DefaultMaxDepth"/>, used for the files that must be rejected.
        ///     Comfortably below where even the large test stack would give out, so a load of it fails
        ///     on the limit rather than by crashing.
        /// </summary>
        private const int BeyondBound = 300;

        /// <summary>
        ///     Runs <paramref name="body"/> on a thread with a 16 MB stack and rethrows whatever it
        ///     threw, stack preserved. See the class remark for why the depth tests need this.
        /// </summary>
        private static void OnDeepStack(Action body) {
            ExceptionDispatchInfo? captured = null;
            var t = new Thread(() => {
                try {
                    body();
                } catch (Exception e) {
                    captured = ExceptionDispatchInfo.Capture(e);
                }
            }, 16 * 1024 * 1024);
            t.Start();
            t.Join();
            captured?.Throw();
        }

        private static Node Chain(int depth) {
            var head = new Node();
            var cur = head;
            for (var i = 1; i < depth; i++) {
                cur.Child = new Node();
                cur = cur.Child;
            }

            return head;
        }

        private static int Measure(Node head) {
            var n = 0;
            for (var c = head; c != null; c = c.Child)
                n++;
            return n;
        }

        /// <summary>
        ///     The JSON a <see cref="DeepSettings"/> holding a chain of <paramref name="depth"/> nodes
        ///     serialises to, built iteratively so that constructing a beyond-the-bound input does not
        ///     itself recurse.
        /// </summary>
        private static string DeepJson(int depth) {
            var sb = new StringBuilder();
            sb.Append("{\"Root\":");
            for (var i = 0; i < depth; i++)
                sb.Append("{\"Child\":");
            sb.Append("null");
            for (var i = 0; i < depth; i++)
                sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        ///     Nesting within the bound round-trips through a real save and load. 64/65/70 are the
        ///     depths that regressed in 2.1.0 under the inherited limit of 64; they must load again.
        ///     100 and 127 confirm real headroom right up to the shipped bound.
        /// </summary>
        [DataTestMethod]
        [DataRow(2)]
        [DataRow(30)]
        [DataRow(63)]
        [DataRow(64)]
        [DataRow(65)]
        [DataRow(70)]
        [DataRow(100)]
        [DataRow(127)]
        public void GraphsWithinTheBound_RoundTrip(int depth) {
            using var f = new TempFile();
            OnDeepStack(() => {
                var w = JsonSettings.Configure<DeepSettings>(f).LoadNow();
                w.Root = Chain(depth);
                w.Save();

                var r = JsonSettings.Configure<DeepSettings>(f).LoadNow();
                Measure(r.Root).Should().Be(depth);
            });
        }

        /// <summary>
        ///     A file nested past the bound is rejected on load - the backstop is real, not decorative
        ///     - and it is rejected as a <see cref="JsonSettingsException"/>, the type a caller catches
        ///     around a load, rather than by exhausting the stack.
        /// </summary>
        [TestMethod]
        public void FilesDeeperThanTheBound_AreRejectedOnLoad() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, DeepJson(BeyondBound));

            new Action(() => OnDeepStack(() => JsonSettings.Configure<DeepSettings>(f).LoadNow()))
                .Should().Throw<JsonSettingsException>("a bound that never fires is not a bound");
        }

        /// <summary>
        ///     Depth through collections rather than an object chain, within the bound.
        /// </summary>
        /// <remarks>
        ///     Included because a settings class rarely nests dozens of objects deliberately, but a
        ///     recursive tree, a nested dictionary of groups, or an arbitrarily-shaped
        ///     <c>List&lt;object&gt;</c> reaches that depth from data rather than from design - the
        ///     realistic way to approach the limit.
        ///
        ///     45 collection levels, not 70, because a <c>List&lt;object&gt;</c> under
        ///     TypeNameHandling.Auto serialises as <c>{"$type":...,"$values":[...]}</c> - an object AND
        ///     an array - so each level costs about two of Json.NET's counted levels. 45 lands near 90
        ///     counted: comfortably past the inherited limit of 64 that would have rejected it, and
        ///     comfortably inside 128. (That two-per-level cost is itself a reason 128 is not stingy:
        ///     it is ~64 nested collections, far more than any real settings file carries.)
        /// </remarks>
        [TestMethod]
        public void DeeplyNestedCollections_WithinTheBound_RoundTrip() {
            using var f = new TempFile();

            OnDeepStack(() => {
                var head = new List<object>();
                var cur = head;
                for (var i = 1; i < 45; i++) {
                    var next = new List<object>();
                    cur.Add(next);
                    cur = next;
                }

                var w = JsonSettings.Configure<ListSettings>(f).LoadNow();
                w.Root = head;
                w.Save();

                new Action(() => JsonSettings.Configure<ListSettings>(f).LoadNow())
                    .Should().NotThrow("collection nesting counts toward the same limit as object nesting");
            });
        }

        /// <summary>
        ///     The limit is on reading, not writing - which is why the inherited cap was so easy to
        ///     miss: an application can persist a graph and only discover on next start that it can no
        ///     longer read it back.
        /// </summary>
        /// <remarks>
        ///     Demonstrated by moving the limit rather than the data: a 100-node graph saves regardless
        ///     of the limit (writing ignores MaxDepth), yet reading the very same file back throws once
        ///     the limit is dropped below its depth.
        /// </remarks>
        [TestMethod]
        public void TheLimitAppliesToReadingNotWriting() {
            using var f = new TempFile();
            var saved = JsonSettings.SerializationSettings.MaxDepth;
            JsonSettings.SerializationSettings.MaxDepth = 80;
            try {
                OnDeepStack(() => {
                    var w = JsonSettings.Configure<DeepSettings>(f).LoadNow();
                    w.Root = Chain(100);
                    new Action(() => w.Save()).Should().NotThrow("writing is not depth-limited");

                    new Action(() => JsonSettings.Configure<DeepSettings>(f).LoadNow())
                        .Should().Throw<JsonSettingsException>("reading past the limit is");
                });
            } finally {
                JsonSettings.SerializationSettings.MaxDepth = saved;
            }
        }

        /// <summary>
        ///     The default this build ships. Stated directly rather than inferred from a failure, so
        ///     that a future Newtonsoft bump silently changing it again fails HERE rather than in a
        ///     depth round-trip whose message points at the wrong thing.
        /// </summary>
        [TestMethod]
        public void DefaultDepthLimit_IsTheGenerousBound() {
            JsonSettings.SerializationSettings.MaxDepth
                        .Should().Be(DefaultMaxDepth, "the limit is set explicitly and must not drift back to the dependency's default");
        }

        /// <summary>
        ///     The escape hatch back to literal 2.0.x behaviour: MaxDepth = null loads a file nested
        ///     past the shipped bound.
        /// </summary>
        [TestMethod]
        public void SettingMaxDepthToNull_RestoresUnlimitedNesting() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, DeepJson(BeyondBound));

            var saved = JsonSettings.SerializationSettings.MaxDepth;
            JsonSettings.SerializationSettings.MaxDepth = null;
            try {
                new Action(() => OnDeepStack(() => JsonSettings.Configure<DeepSettings>(f).LoadNow()))
                    .Should().NotThrow("null is unlimited, which is what a consumer needing deep graphs sets");
            } finally {
                JsonSettings.SerializationSettings.MaxDepth = saved;
            }
        }

        public class DeepSettings : JsonSettings {
            public override string FileName { get; set; }
            public Node Root { get; set; }
        }

        public class ListSettings : JsonSettings {
            public override string FileName { get; set; }
            public List<object> Root { get; set; }
        }

        public class Node {
            public Node Child { get; set; }
            public int V { get; set; }
        }
    }
}
