using Plutus.Contracts;
using Plutus.Entities;
using System.Threading.Tasks;

namespace Plutus.Repository
{
    public abstract class RepositoryWrapper : IRepositoryWrapper
    {
        #region Fields
        protected readonly RepositoryContext repositoryContext;
        protected IAuthActionRepository authActionRepository;
        protected ICategoryRepository categoryRepository;
        protected ICheckoutItemChangeRepository checkoutItemChangeRepository;
        protected IDiscountRepository discountRepository;
        protected IEmployeeRepository employeeRepository;
        protected ITillRepository tillRepository;
        protected IItemRepository itemRepository;
        protected INoteRepository noteRepository;
        protected IPaymentMethodRepository paymentMethodRepository;
        protected IRefundRepository refundRepository;
        protected ISaleRepository saleRepository;
        protected ISavedTransactionRepository savedTransactionRepository;
        protected IStockRepository stockRepository;
        protected IStoreRepository storeRepository;
        protected ITaxRepository taxRepository;
        protected ITransactionRepository transactionRepository;
        protected IBussinessRepository bussinessRepository;
        protected IRoleRepository roleRepository;

        #endregion

        #region Properties
        public virtual IAuthActionRepository AuthActionRepository => authActionRepository ??= new AuthActionRepository(repositoryContext);
        public virtual ICategoryRepository CategoryRepository => categoryRepository ??= new CategoryRepository(repositoryContext);

        public virtual IRoleRepository RoleRepository => roleRepository ??= new RoleRepository(repositoryContext);

        public virtual ICheckoutItemChangeRepository CheckoutItemChangeRepository => checkoutItemChangeRepository ??= new CheckoutItemChangeRepository(repositoryContext);
        public virtual IDiscountRepository DiscountRepository => discountRepository ??= new DiscountRepository(repositoryContext);
        public virtual IEmployeeRepository EmployeeRepository => employeeRepository ??= new EmployeeRepository(repositoryContext);
        public virtual ITillRepository TillRepository => tillRepository ??= new TillRepository(repositoryContext);
        public virtual IItemRepository ItemRepository => itemRepository ??= new ItemRepository(repositoryContext);
        public virtual INoteRepository NoteRepository => noteRepository ??= new NoteRepository(repositoryContext);
        public virtual IPaymentMethodRepository PaymentMethodRepository => paymentMethodRepository ??= new PaymentMethodRepository(repositoryContext);
        public virtual IRefundRepository RefundRepository => refundRepository ??= new RefundRepository(repositoryContext);
        public virtual ISaleRepository SaleRepository => saleRepository ??= new SaleRepository(repositoryContext);
        public virtual ISavedTransactionRepository SavedTransactionRepository => savedTransactionRepository ??= new SavedTransactionsRepository(repositoryContext);
        public virtual IStockRepository StockRepository => stockRepository ??= new StockRepository(repositoryContext);
        public virtual IStoreRepository StoreRepository => storeRepository ??= new StoreRepository(repositoryContext);
        public virtual ITaxRepository TaxRepository => taxRepository ??= new TaxRepository(repositoryContext);
        public virtual ITransactionRepository TransactionRepository => transactionRepository ??= new TransactionRepository(repositoryContext);
        public virtual IBussinessRepository BussinessRepository => bussinessRepository ??= new BussinessRepository(repositoryContext);

        #endregion
        public RepositoryWrapper(RepositoryContext repositoryContext)
        {
            this.repositoryContext = repositoryContext;
        }

        public int Save() => repositoryContext.SaveChanges();

        public Task<int> SaveAsync() => repositoryContext.SaveChangesAsync();

        public void SetSyncState(bool syncState = false) => repositoryContext.SetSyncState(syncState);

        public void SetCurrentUser(string currentUserUbjectId) => repositoryContext.CurrentUser = currentUserUbjectId;
        public string GetCurrentUser() => repositoryContext.CurrentUser;

    }
}
