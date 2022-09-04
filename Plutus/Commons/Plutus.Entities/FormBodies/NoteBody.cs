using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class NoteBody : FormBody<Note>
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public Guid SaleId { get; set; }

        public override Note GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.IdOne = Id;
            entity.Text = Text;
            entity.IdTwo = SaleId;
            return entity;
        }

        public NoteBody() : base()
        {

        }

        public NoteBody(Note entity) : base(entity)
        {
            Id = entity.IdOne;
            Text = entity.Text;
            SaleId = entity.IdTwo;
        }
    }
}
