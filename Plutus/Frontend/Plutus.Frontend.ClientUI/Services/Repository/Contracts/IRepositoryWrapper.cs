namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IRepositoryWrapper
    {
        IAuthActionRepository AuthActionRepository { get; }
        IBusinessRepository BusinessRepository { get; }
        ICategoryRepository CategoryRepository { get; }
        ICheckoutItemChangeRepository CheckoutItemChangeRepository { get; }
        IDiscountRepository DiscountRepository { get; }
        IEmployeeRepository EmployeeRepository { get; }
        IItemRepository ItemRepository { get; }
        INoteRepository NoteRepository { get; }
        IPaymentMethodRepository PaymentMethodRepository { get; }
        IRefundRepository RefundRepository { get; }
        IRoleRepository RoleRepository { get; }
        ISaleRepository SaleRepository { get; }
        ISavedTransactionRepository SavedTransactionRepository { get; }
        IStockRepository StockRepository { get; }
        IStoreRepository StoreRepository { get; }
        ITaxRepository TaxRepository { get; }
        ITillRepository TillRepository { get; }
        ITransactionRepository TransactionRepository { get; }
        string GetCurrentUser();

        int Save();

        Task<int> SaveAsync();

        void SetCurrentUser(string currentUserObjectId);

        void SetSyncState(bool syncState = false);
    }
}
