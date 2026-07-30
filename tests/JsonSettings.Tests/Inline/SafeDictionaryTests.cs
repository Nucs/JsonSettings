using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Collections;

namespace Nucs.JsonSettings.Tests.Inline {
    /// <summary>
    ///     Unit tests for the internal <see cref="SafeDictionary{TKey,TValue}"/> that backs
    ///     <see cref="SettingsBag"/>. The bag round-trip tests exercise it indirectly, but its own
    ///     surface -- the default-returning indexer, the overwrite-<see cref="SafeDictionary{TKey,TValue}.Add"/>,
    ///     <see cref="SafeDictionary{TKey,TValue}.FindKeyByValue"/>, <see cref="SafeDictionary{TKey,TValue}.Clone"/>
    ///     and the constructor overloads -- was almost entirely uncovered.
    /// </summary>
    [TestClass]
    public class SafeDictionaryTests {
        [TestMethod]
        public void Indexer_MissingKey_ReturnsDefault_NotThrow() {
            var d = new SafeDictionary<string, int>();
            //ConcurrentDictionary's own indexer would throw KeyNotFoundException; the SafeDictionary
            //override returns default(TValue) instead.
            d["missing"].Should().Be(0);

            var refD = new SafeDictionary<string, string>();
            refD["missing"].Should().BeNull();
        }

        [TestMethod]
        public void Indexer_Set_ThenGet_Roundtrips() {
            var d = new SafeDictionary<string, int>();
            d["a"] = 42;
            d["a"].Should().Be(42);
        }

        [TestMethod]
        public void Add_AddsOrOverwrites_WithoutThrowing() {
            var d = new SafeDictionary<string, int>();
            d.Add("k", 1);
            d["k"].Should().Be(1);

            //Unlike Dictionary.Add / ConcurrentDictionary.TryAdd, this Add overwrites an existing key
            //rather than throwing, which is what a settings bag needs for a set-or-update assignment.
            d.Add("k", 2);
            d["k"].Should().Be(2);
        }

        [TestMethod]
        public void FindKeyByValue_Found_ReturnsKey() {
            var d = new SafeDictionary<string, int>();
            d.Add("one", 1);
            d.Add("two", 2);
            d.FindKeyByValue(2).Should().Be("two");
        }

        [TestMethod]
        public void FindKeyByValue_NotFound_ReturnsDefaultKey() {
            var d = new SafeDictionary<string, int>();
            d.Add("one", 1);
            //No value 999 present -> default(TKey), which is null for a reference-typed key.
            d.FindKeyByValue(999).Should().BeNull();

            var valueKeyed = new SafeDictionary<int, string>();
            valueKeyed.Add(5, "five");
            //default(int) == 0 for a value-typed key when nothing matches.
            valueKeyed.FindKeyByValue("absent").Should().Be(0);
        }

        [TestMethod]
        public void Clone_ProducesIndependentCopy() {
            var d = new SafeDictionary<string, int>();
            d.Add("a", 1);
            d.Add("b", 2);

            var clone = d.Clone();
            clone.Should().NotBeSameAs(d);
            clone["a"].Should().Be(1);
            clone["b"].Should().Be(2);

            //Mutating the clone must not touch the original.
            clone["a"] = 100;
            d["a"].Should().Be(1);
            clone["a"].Should().Be(100);
        }

        [TestMethod]
        public void Constructor_FromCollection_CopiesEntries() {
            var seed = new[] {
                new KeyValuePair<string, int>("x", 10),
                new KeyValuePair<string, int>("y", 20),
            };
            var d = new SafeDictionary<string, int>(seed);
            d["x"].Should().Be(10);
            d["y"].Should().Be(20);
        }

        [TestMethod]
        public void Constructor_WithComparer_HonoursComparer() {
            var d = new SafeDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d.Add("Key", 1);
            //Case-insensitive comparer means the differently-cased lookup finds the same entry.
            d["KEY"].Should().Be(1);
        }

        [TestMethod]
        public void Constructor_FromCollectionWithComparer_HonoursComparer() {
            var seed = new[] { new KeyValuePair<string, int>("Key", 7) };
            var d = new SafeDictionary<string, int>(seed, StringComparer.OrdinalIgnoreCase);
            d["kEy"].Should().Be(7);
        }

        [TestMethod]
        public void Constructor_WithConcurrencyAndCapacity_Works() {
            var d = new SafeDictionary<string, int>(concurrencyLevel: 2, capacity: 8);
            d.Add("a", 1);
            d["a"].Should().Be(1);
        }

        [TestMethod]
        public void Constructor_WithConcurrencyCapacityAndComparer_Works() {
            var d = new SafeDictionary<string, int>(2, 8, StringComparer.OrdinalIgnoreCase);
            d.Add("A", 1);
            d["a"].Should().Be(1);
        }

        [TestMethod]
        public void Constructor_WithConcurrencyCollectionAndComparer_Works() {
            var seed = new[] { new KeyValuePair<string, int>("A", 1) };
            var d = new SafeDictionary<string, int>(2, seed, StringComparer.OrdinalIgnoreCase);
            d["a"].Should().Be(1);
        }
    }
}
