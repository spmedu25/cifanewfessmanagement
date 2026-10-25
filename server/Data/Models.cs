using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace CoachingApp.Data;

public enum StudentStatus { Pending, Active, Left, Completed }
public enum FeeType { Admission, Monthly }

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Batch> Batches { get; set; } = new();
}

public class Batch
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string Course { get; set; } = "";   // ID card pe "Course"
    [Range(1, 60)] public int DurationMonths { get; set; } = 12;
    [Range(0, 10000000)] public decimal AdmissionFee { get; set; }
    [Range(0, 10000000)] public decimal MonthlyFee { get; set; }
    [Range(1, 5000)] public int Capacity { get; set; } = 60;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;
    public List<Student> Students { get; set; } = new();
}

public class Student
{
    public int Id { get; set; }
    public int RollNo { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(120)] public string FatherName { get; set; } = "";
    [MaxLength(120)] public string? MotherName { get; set; }
    [MaxLength(300)] public string Address { get; set; } = "";
    [MaxLength(15)] public string Mobile { get; set; } = "";
    [MaxLength(15)] public string? AltMobile { get; set; }
    [MaxLength(120)] public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
    [MaxLength(10)] public string Gender { get; set; } = "Male";
    [MaxLength(20)] public string? AadhaarNo { get; set; }
    public string? PhotoPath { get; set; }
    public DateTime AdmissionDate { get; set; } = DateTime.Today;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public decimal MonthlyDiscount { get; set; }     // har month ki fees me discount
    public decimal AdmissionDiscount { get; set; }   // admission fee me discount
    public StudentStatus Status { get; set; } = StudentStatus.Pending;
    public string? PasswordHash { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<FeeInstallment> Installments { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

public class FeeInstallment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public FeeType Type { get; set; }
    public int MonthNo { get; set; }
    [MaxLength(80)] public string Title { get; set; } = "";
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal Waived { get; set; }          // settlement me maaf ki gayi amount
    public decimal PaidAmount { get; set; }
    [NotMapped] public decimal Net => Amount - Discount - Waived;
    [NotMapped] public decimal Balance => Amount - Discount - Waived - PaidAmount;
}

public class Payment
{
    public int Id { get; set; }
    [MaxLength(30)] public string ReceiptNo { get; set; } = "";
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaidOn { get; set; } = DateTime.Now;
    [MaxLength(20)] public string Mode { get; set; } = "Cash";
    [MaxLength(100)] public string? Reference { get; set; }
    [MaxLength(300)] public string? Remarks { get; set; }
    [MaxLength(100)] public string ReceivedBy { get; set; } = "";
    public bool IsCancelled { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = new();
}

public class PaymentAllocation
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public int InstallmentId { get; set; }
    public FeeInstallment Installment { get; set; } = null!;
    public decimal Amount { get; set; }
}

public class AdminUser
{
    public int Id { get; set; }
    [MaxLength(50)] public string Username { get; set; } = "";
    [MaxLength(120)] public string FullName { get; set; } = "Administrator";
    public string PasswordHash { get; set; } = "";
}

public class InstituteSettings
{
    public int Id { get; set; } = 1;
    [MaxLength(150)] public string Name { get; set; } = "COMPUTER INSTITUTE & LIBRARY RURA";
    [MaxLength(150)] public string? Tagline { get; set; }
    [MaxLength(150)] public string Phones { get; set; } = "9935268529,6386324371";
    [MaxLength(150)] public string Website { get; set; } = "www.cifacomputer.in";
    [MaxLength(300)] public string? Address { get; set; }
    public string? LogoPath { get; set; }
    public string? SignPath { get; set; }
    public string? StampPath { get; set; }
    [MaxLength(9)] public string CardColor1 { get; set; } = "#4aa3df";
    [MaxLength(9)] public string CardColor2 { get; set; } = "#7fd1c0";
    [MaxLength(150)] public string FeeCardNote { get; set; } = "Fees once paid is not refundable";
    public int NextRollNo { get; set; } = 10001;
}

// Admission form (Blazor + API dono me use hota hai)
public class StudentInput
{
    [Required(ErrorMessage = "Name required")] public string Name { get; set; } = "";
    [Required(ErrorMessage = "Father's name required")] public string FatherName { get; set; } = "";
    public string? MotherName { get; set; }
    [Required(ErrorMessage = "Address required")] public string Address { get; set; } = "";
    [Required(ErrorMessage = "Mobile required"), RegularExpression(@"^\d{10}$", ErrorMessage = "Mobile 10 digit ka hona chahiye")]
    public string Mobile { get; set; } = "";
    public string? AltMobile { get; set; }
    public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = "Male";
    public string? AadhaarNo { get; set; }
    public DateTime AdmissionDate { get; set; } = DateTime.Today;
    [Range(1, int.MaxValue, ErrorMessage = "Batch select karein")] public int BatchId { get; set; }
    [Range(0, 10000000)] public decimal MonthlyDiscount { get; set; }
    [Range(0, 10000000)] public decimal AdmissionDiscount { get; set; }
    public string? Remarks { get; set; }
    public string? Password { get; set; }

    public void ApplyTo(Student s)
    {
        s.Name = Name.Trim(); s.FatherName = FatherName.Trim(); s.MotherName = MotherName?.Trim();
        s.Address = Address.Trim(); s.Mobile = Mobile.Trim(); s.AltMobile = AltMobile?.Trim();
        s.Email = Email?.Trim(); s.DateOfBirth = DateOfBirth; s.Gender = Gender; s.AadhaarNo = AadhaarNo?.Trim();
        s.AdmissionDate = AdmissionDate.Date; s.BatchId = BatchId;
        s.MonthlyDiscount = MonthlyDiscount; s.AdmissionDiscount = AdmissionDiscount; s.Remarks = Remarks;
    }
    public static StudentInput From(Student s) => new()
    {
        Name = s.Name, FatherName = s.FatherName, MotherName = s.MotherName, Address = s.Address, Mobile = s.Mobile,
        AltMobile = s.AltMobile, Email = s.Email, DateOfBirth = s.DateOfBirth, Gender = s.Gender, AadhaarNo = s.AadhaarNo,
        AdmissionDate = s.AdmissionDate, BatchId = s.BatchId, MonthlyDiscount = s.MonthlyDiscount,
        AdmissionDiscount = s.AdmissionDiscount, Remarks = s.Remarks
    };
}

public class ChatMessage
{
    public int Id { get; set; }
    public int StudentId { get; set; }          // har student ka admin ke saath ek chat thread
    public Student Student { get; set; } = null!;
    public bool FromAdmin { get; set; }
    [MaxLength(2000)] public string? Text { get; set; }
    public string? AttachmentPath { get; set; }
    public DateTime SentAt { get; set; } = DateTime.Now;
    public DateTime? ReadAt { get; set; }
}

public class DeviceToken
{
    public int Id { get; set; }
    [MaxLength(400)] public string Token { get; set; } = "";
    public bool IsAdmin { get; set; }
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    [MaxLength(20)] public string? Platform { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public enum SettleScope { Overdue, All }

// Settlement / round-off: baaki hisab close karna (kuch amount lekar baaki maaf)
public class Settlement
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public DateTime SettledOn { get; set; } = DateTime.Now;
    public SettleScope Scope { get; set; }
    public decimal BalanceBefore { get; set; }   // settlement se pehle is scope ka baaki
    public decimal PaidAmount { get; set; }      // student ne diya
    public decimal WaivedAmount { get; set; }    // maaf kiya
    public int? PaymentId { get; set; }          // jo payment/receipt bani
    public bool ClosedAccount { get; set; }
    [MaxLength(300)] public string? Remarks { get; set; }
    [MaxLength(100)] public string SettledBy { get; set; } = "";
    public bool IsCancelled { get; set; }
    public DateTime? CancelledOn { get; set; }
    public List<SettlementAllocation> Allocations { get; set; } = new();
}

public class SettlementAllocation
{
    public int Id { get; set; }
    public int SettlementId { get; set; }
    public Settlement Settlement { get; set; } = null!;
    public int InstallmentId { get; set; }
    public FeeInstallment Installment { get; set; } = null!;
    public decimal Amount { get; set; }
}
