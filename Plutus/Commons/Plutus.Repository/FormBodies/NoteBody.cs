using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class NoteBody : FormBody<Note>
    {
        public string Text { get; set; }

        public override Note GenerateEntity() => new Note
        {
            Text = Text
        };
    }
}
