using Plutus.Entities.Enums;

namespace Plutus.Entities.Models.Interface
{
    public interface IEmp_AuthActions : IAuditable
    {
        Permissions Permissions { get; set; }

        int AuthAId { get; set; }

        Guid EmpId { get; set; }
    }
}
