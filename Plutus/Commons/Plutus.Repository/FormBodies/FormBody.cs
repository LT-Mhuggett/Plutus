namespace Plutus.Repository.FormBodies
{
    public abstract class FormBody<TEntity>
    {

        public abstract TEntity GenerateEntity();
    }
}
