using System.Threading.Tasks;

namespace Plutus.Contracts
{
    public interface IRepositoryWrapper
    {
        IAuthActionRepository AuthActionRepository { get; }
        ICategoryRepository CategoryRepository { get; }
        ICheckoutItemChangeRepository CheckoutItemChangeRepository { get; }
        IDiscountRepository DiscountRepository { get; }
        IEmployeeRepository EmployeeRepository { get; }
        IItemRepository ItemRepository { get; }
        INoteRepository NoteRepository { get; }
        IPaymentMethodRepository PaymentMethodRepository { get; }
        IPaymentMethod_SaleRepository PaymentMethod_SaleRepository { get; }
        IRefundRepository RefundRepository { get; }
        ISaleRepository SaleRepository { get; }
        ISavedTransactionRepository SavedTransactionRepository { get; }
        IStockRepository StockRepository { get; }
        IStoreRepository StoreRepository { get; }
        ITaxRepository TaxRepository { get; }
        ITransactionRepository TransactionRepository { get; }

        int Save();

        Task<int> SaveAsync();

        void SetSyncState(bool syncState = false);
    }
}
