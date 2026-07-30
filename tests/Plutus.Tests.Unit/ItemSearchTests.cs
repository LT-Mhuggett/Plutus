using System;
using System.Linq;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using Xunit;

namespace Plutus.Tests.Unit
{
    /// <summary>
    /// Till item search (ItemParameters.GetExpression). 2026-07-30: "batman one" could not
    /// find "Batman Year One" because Search was matched as one whole substring. MatchAllWords
    /// splits on whitespace and requires every word (each against name/barcode/brand);
    /// default false preserves the original whole-phrase behaviour.
    /// </summary>
    public class ItemSearchTests
    {
        private static Item MakeItem(string name, string idOne = "X", string brand = "-") => new()
        {
            Name = name,
            IdOne = idOne,
            Brand = brand,
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
        };

        private static bool Matches(ItemParameters p, Item item) =>
            new[] { item }.AsQueryable().Where(p.GetExpression()).Any();

        [Fact]
        public void Whole_phrase_mode_misses_interleaved_words()
        {
            var p = new ItemParameters { Search = "Batman one" };
            Assert.False(Matches(p, MakeItem("Batman Year One")));
        }

        [Fact]
        public void Match_all_words_finds_interleaved_words()
        {
            var p = new ItemParameters { Search = "Batman one", MatchAllWords = true };
            Assert.True(Matches(p, MakeItem("Batman Year One")));
        }

        [Fact]
        public void Match_all_words_requires_every_word()
        {
            var p = new ItemParameters { Search = "Batman two", MatchAllWords = true };
            Assert.False(Matches(p, MakeItem("Batman Year One")));
        }

        [Fact]
        public void Match_all_words_is_case_insensitive_and_spans_fields()
        {
            // one word hits the brand, the other the name
            var p = new ItemParameters { Search = "dc batman", MatchAllWords = true };
            Assert.True(Matches(p, MakeItem("Batman Year One", brand: "DC Comics")));
        }

        [Fact]
        public void Single_word_behaves_identically_in_both_modes()
        {
            var item = MakeItem("Batman Year One");
            Assert.True(Matches(new ItemParameters { Search = "batman" }, item));
            Assert.True(Matches(new ItemParameters { Search = "batman", MatchAllWords = true }, item));
        }
    }
}
