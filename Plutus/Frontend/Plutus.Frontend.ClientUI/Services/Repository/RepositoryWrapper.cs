using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Entities;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Core;
using Microsoft.EntityFrameworkCore.Storage;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class RepositoryWrapper : IRepositoryWrapper
    {
        #region Fields
        private readonly static SemaphoreSlim _globalRepositorySemaphore = new(1, 1);
        private readonly AppSettings _appSettings;
        private readonly ILogger _logger;
        private readonly RepositoryContext _repositoryContext;
        private readonly IAppState _appState;
        private IAuthActionRepository _authActionRepository;
        private IBusinessRepository _businessRepository;
        private ICategoryRepository _categoryRepository;
        private ICheckoutItemChangeRepository _checkoutItemChangeRepository;
        private IDiscountRepository _discountRepository;
        private IEmployeeRepository _employeeRepository;
        private IItemRepository _itemRepository;
        private INoteRepository _noteRepository;
        private IPaymentMethodRepository _paymentMethodRepository;
        private IRefundRepository _refundRepository;
        private IRoleRepository _roleRepository;
        private ISaleRepository _saleRepository;
        private ISavedTransactionRepository _savedTransactionRepository;
        private IStockRepository _stockRepository;
        private IStoreRepository _storeRepository;
        private ITaxRepository _taxRepository;
        private ITillRepository _tillRepository;
        private ITransactionRepository _transactionRepository;
        #endregion

        #region Properties
        public IAuthActionRepository AuthActionRepository => _authActionRepository ??= new AuthActionRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IBusinessRepository BusinessRepository => _businessRepository ??= new BusinessRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ICategoryRepository CategoryRepository => _categoryRepository ??= new CategoryRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ICheckoutItemChangeRepository CheckoutItemChangeRepository => _checkoutItemChangeRepository ??= new CheckoutItemChangeRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IDiscountRepository DiscountRepository => _discountRepository ??= new DiscountRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IEmployeeRepository EmployeeRepository => _employeeRepository ??= new EmployeeRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IItemRepository ItemRepository => _itemRepository ??= new ItemRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public INoteRepository NoteRepository => _noteRepository ??= new NoteRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IPaymentMethodRepository PaymentMethodRepository => _paymentMethodRepository ??= new PaymentMethodRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IRefundRepository RefundRepository => _refundRepository ??= new RefundRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IRoleRepository RoleRepository => _roleRepository ??= new RoleRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ISaleRepository SaleRepository => _saleRepository ??= new SaleRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ISavedTransactionRepository SavedTransactionRepository => _savedTransactionRepository ??= new SavedTransactionRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IStockRepository StockRepository => _stockRepository ??= new StockRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public IStoreRepository StoreRepository => _storeRepository ??= new StoreRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ITaxRepository TaxRepository => _taxRepository ??= new TaxRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ITillRepository TillRepository => _tillRepository ??= new TillRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        public ITransactionRepository TransactionRepository => _transactionRepository ??= new TransactionRepository(_repositoryContext, _globalRepositorySemaphore, _appSettings, _logger, _appState);
        #endregion

        public RepositoryWrapper(SqliteDbContext context, IConfiguration configuration, ILogger logger, IAppState appState)
        {
            _repositoryContext = context;
            _appSettings = configuration.GetRequiredSection("AppSettings").Get<AppSettings>();
            _logger = logger;
            _repositoryContext.Database.Migrate();
            _appState = appState;
            //else
            //{
            //Ask which user is making the call
            //Also think about implementing a second constructor with employee ID passed to it
            //}
        }

        public string GetCurrentUser() => throw new NotSupportedException();

        public int Save() => _repositoryContext.SaveChanges();

        public Task<int> SaveAsync() => _repositoryContext.SaveChangesAsync();

        public void SetCurrentUser(string currentUserObjectId) => _repositoryContext.CurrentUser = currentUserObjectId;

        public void SetSyncState(bool syncState = false) => _repositoryContext.SaveChangesAsync(syncState);

        public Task<IDbContextTransaction> GetDbContextTransaction() => _repositoryContext.Database.BeginTransactionAsync();

        public Task RollBackTransaction(IDbContextTransaction dbContextTransaction) => dbContextTransaction.RollbackAsync();

        public Task CommitTransaction(IDbContextTransaction dbContextTransaction) => dbContextTransaction.CommitAsync();
    }
}
