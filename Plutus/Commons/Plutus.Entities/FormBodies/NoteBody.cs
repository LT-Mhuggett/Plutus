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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public NoteBody() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
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
