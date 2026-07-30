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

        // ── FE8.1: quoted segments are literal phrases, composing with words ──

        [Fact]
        public void Quoted_phrase_is_literal_even_in_word_mode()
        {
            var p = new ItemParameters { Search = "\"batman one\"", MatchAllWords = true };
            Assert.False(Matches(p, MakeItem("Batman Year One")));
            Assert.True(Matches(p, MakeItem("Batman One Bad Day")));
        }

        [Fact]
        public void Mixed_phrase_and_word_compose()
        {
            var p = new ItemParameters { Search = "\"year one\" batman", MatchAllWords = true };
            Assert.True(Matches(p, MakeItem("Batman: Year One")));
            Assert.False(Matches(p, MakeItem("Spider-Man: Year One"))); // phrase hits, word doesn't
            Assert.False(Matches(p, MakeItem("Batman: One Year Later"))); // word hits, phrase doesn't
        }

        [Fact]
        public void Unclosed_quote_runs_to_end_of_input()
        {
            var p = new ItemParameters { Search = "\"year one", MatchAllWords = true };
            Assert.True(Matches(p, MakeItem("Batman: Year One")));
            Assert.False(Matches(p, MakeItem("Batman: One Year Later")));
        }

        [Fact]
        public void Quotes_only_input_matches_everything()
        {
            // no tokens → only the date-window base filter applies
            var p = new ItemParameters { Search = "\"\"", MatchAllWords = true };
            Assert.True(Matches(p, MakeItem("Anything")));
        }

        [Fact]
        public void Phrase_mode_strips_quotes_and_stays_whole_phrase()
        {
            var p = new ItemParameters { Search = "\"batman one\"" }; // MatchAllWords off
            Assert.True(Matches(p, MakeItem("Batman One Bad Day")));
            Assert.False(Matches(p, MakeItem("Batman Year One")));
        }

        // ── FE5.0: server-side category filter ──

        [Fact]
        public void CatId_filters_and_composes_with_search()
        {
            var comics = Guid.NewGuid();
            var toys = Guid.NewGuid();
            var inComics = MakeItem("Batman Year One"); inComics.CatId = comics;
            var inToys = MakeItem("Batman Figure"); inToys.CatId = toys;

            var catOnly = new ItemParameters { CatId = comics };
            Assert.True(Matches(catOnly, inComics));
            Assert.False(Matches(catOnly, inToys));

            var both = new ItemParameters { CatId = toys, Search = "batman", MatchAllWords = true };
            Assert.False(Matches(both, inComics));
            Assert.True(Matches(both, inToys));
        }
    }
}
