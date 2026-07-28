using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Upgrade {
    /// <summary>
    ///     Covers how deeply a settings object may be nested before it stops loading.
    /// </summary>
    /// <remarks>
    ///     WHAT THESE ASSERT. The behaviour of 2.0.1 and 2.0.2, measured. See
    ///     <see cref="ModuleChainingTests"/> for the full note.
    ///
    ///     THE MECHANISM. This is not a change in this library's own code - it is inherited.
    ///     Json.NET 13.0.1 changed the default <see cref="Newtonsoft.Json.JsonSerializerSettings.MaxDepth"/>
    ///     from null (unlimited) to 64, and 2.1.0 upgraded Newtonsoft.Json from 12.0.3 to 13.0.3.
    ///     <see cref="JsonSettings.SerializationSettings"/> never sets MaxDepth, so it takes whatever
    ///     Json.NET's default is at the time.
    ///
    ///     WHY IT IS WORTH TESTING ANYWAY. A dependency's default becoming this library's default is
    ///     still this library's observable behaviour, and it is asymmetric in the worst way: SAVING a
    ///     deep object still works, so a running application writes a file it can no longer read
    ///     back. Nothing warns at the point the damage is done.
    ///
    ///     The boundary rows are here because "64" is not the same as "64 levels of your object":
    ///     Json.NET counts every container it opens, and the settings object itself is the first one.
    /// </remarks>
    [TestClass]
    public class SerializationDepthTests {
        /// <summary>
        ///     Builds a linked chain of <paramref name="depth"/> nodes.
        /// </summary>
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

        private static void SaveThenLoad(string file, int depth) {
            var w = JsonSettings.Configure<DeepSettings>(file).LoadNow();
            w.Root = Chain(depth);
            w.Save();

            var r = JsonSettings.Configure<DeepSettings>(file).LoadNow();
            Measure(r.Root).Should().Be(depth);
        }

        /// <summary>
        ///     Nesting that stayed within the new limit. Green on both versions; brackets the rows
        ///     below so a failure there cannot be blamed on the chain builder.
        /// </summary>
        [DataTestMethod]
        [DataRow(2)]
        [DataRow(30)]
        [DataRow(60)]
        [DataRow(63)]
        public void ShallowGraphs_RoundTrip(int depth) {
            using var f = new TempFile();
            SaveThenLoad(f, depth);
        }

        /// <summary>
        ///     Nesting past the Json.NET 13 default.
        /// </summary>
        /// <remarks>
        ///     64 is the first failing row, not the last passing one: the settings object contributes
        ///     the outermost container, so a 64-node chain is 65 levels by Json.NET's count.
        /// </remarks>
        [DataTestMethod]
        [DataRow(64)]
        [DataRow(65)]
        [DataRow(70)]
        [DataRow(200)]
        public void DeepGraphs_RoundTrip(int depth) {
            using var f = new TempFile();
            SaveThenLoad(f, depth);
        }

        /// <summary>
        ///     Depth through collections rather than through an object chain.
        /// </summary>
        /// <remarks>
        ///     Included because a settings class rarely nests 64 objects deliberately, but a
        ///     recursive tree, a nested dictionary of groups, or an arbitrarily-shaped
        ///     <c>List&lt;object&gt;</c> reaches that depth from data rather than from design - which
        ///     is the realistic way to hit this.
        /// </remarks>
        [TestMethod]
        public void DeeplyNestedCollections_RoundTrip() {
            using var f = new TempFile();

            var head = new List<object>();
            var cur = head;
            for (var i = 1; i < 70; i++) {
                var next = new List<object>();
                cur.Add(next);
                cur = next;
            }

            var w = JsonSettings.Configure<ListSettings>(f).LoadNow();
            w.Root = head;
            w.Save();

            new Action(() => JsonSettings.Configure<ListSettings>(f).LoadNow())
                .Should().NotThrow("collection nesting counts toward the same limit as object nesting");
        }

        /// <summary>
        ///     Saving is never limited - only loading is.
        /// </summary>
        /// <remarks>
        ///     Green on both versions, and the reason the change is easy to miss: an application can
        ///     run for its whole lifetime writing a file it will fail to read on next start.
        /// </remarks>
        [TestMethod]
        public void SavingADeepGraph_AlwaysSucceeds() {
            using var f = new TempFile();

            var w = JsonSettings.Configure<DeepSettings>(f).LoadNow();
            w.Root = Chain(200);

            new Action(() => w.Save()).Should().NotThrow("the depth limit applies to reading, not writing");
        }

        /// <summary>
        ///     The default itself, stated directly rather than inferred from a failure.
        /// </summary>
        [TestMethod]
        public void SerializationSettings_ImposeNoDepthLimitByDefault() {
            JsonSettings.SerializationSettings.MaxDepth
                        .Should().BeNull("2.0.x placed no limit on how deep a settings object could be");
        }

        /// <summary>
        ///     The documented restore. Green on both versions - on 2.0.x because it is already the
        ///     default, on 2.1.0 because it undoes the change.
        /// </summary>
        [TestMethod]
        public void SettingMaxDepthToNull_RestoresUnlimitedNesting() {
            using var f = new TempFile();
            var saved = JsonSettings.SerializationSettings.MaxDepth;
            JsonSettings.SerializationSettings.MaxDepth = null;
            try {
                SaveThenLoad(f, 70);
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
