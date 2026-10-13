using System;
using System.Security.Claims;
using ExpenseHub.Api.Contracts;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Verifica regras de reembolso sem banco ou rede.</summary>
[TestClass]
public sealed class ExpenseRulesTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Estados incompatÃ­veis impedem aprovaÃ§Ã£o e nÃ£o alteram histÃ³rico.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void ApprovalRejectsInvalidState(int state)
    {
        var expense = Expense((ExpenseState)state);
        BusinessException error = Assert.Throws<BusinessException>(() => ExpenseRules.Decide(expense, User("approver", Roles.Approver), false, null, _now));
        Assert.AreEqual(409, error.StatusCode);
        Assert.AreEqual((ExpenseState)state, expense.State);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>Aprovar registra ator, instante e transiÃ§Ã£o e rejeita repetiÃ§Ã£o.</summary>
    [TestMethod]
    public void ApprovalRecordsHistoryAndRejectsRepeatedDecision()
    {
        var expense = Expense(ExpenseState.Submitted);
        ClaimsPrincipal actor = User("approver", Roles.Approver);
        ExpenseRules.Decide(expense, actor, false, null, _now);
        Assert.AreEqual(ExpenseState.Approved, expense.State);
        Assert.HasCount(1, expense.History);
        Assert.AreEqual("approver", expense.History[0].ActorId);
        Assert.AreEqual(_now, expense.History[0].OccurredAt);
        Assert.AreEqual(ExpenseState.Submitted, expense.History[0].PreviousState);
        Assert.AreEqual(ExpenseState.Approved, expense.History[0].NextState);
        Assert.AreEqual(409, Assert.Throws<BusinessException>(() => ExpenseRules.Decide(expense, actor, false, null, _now)).StatusCode);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>ProprietÃ¡rio nÃ£o decide mesmo acumulando roles.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnerCannotApproveOrReject(bool reject)
    {
        var expense = Expense(ExpenseState.Submitted);
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.Decide(expense, User("owner", Roles.Employee, Roles.Approver), reject, "Justificativa vÃ¡lida", _now)).StatusCode);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>Somente o perfil Approver decide.</summary>
    [TestMethod]
    [DataRow("Employee")]
    [DataRow("Finance")]
    [DataRow("Auditor")]
    [DataRow("Admin")]
    public void WrongRoleCannotDecide(string role)
    {
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.Decide(Expense(ExpenseState.Submitted), User("other", role), false, null, _now)).StatusCode);
    }

    /// <summary>Justificativas fora dos limites sÃ£o rejeitadas.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(9)]
    [DataRow(501)]
    public void RejectionRequiresValidReason(int length)
    {
        var expense = Expense(ExpenseState.Submitted);
        Assert.AreEqual(400, Assert.Throws<BusinessException>(() => ExpenseRules.Decide(expense, User("approver", Roles.Approver), true, new string('x', length), _now)).StatusCode);
        Assert.AreEqual(ExpenseState.Submitted, expense.State);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>ReprovaÃ§Ã£o persiste justificativa e estado final.</summary>
    [TestMethod]
    [DataRow(10)]
    [DataRow(500)]
    public void ValidRejectionRecordsReason(int length)
    {
        var expense = Expense(ExpenseState.Submitted);
        string reason = new('x', length);
        ExpenseRules.Decide(expense, User("approver", Roles.Approver), true, reason, _now);
        Assert.AreEqual(ExpenseState.Rejected, expense.State);
        Assert.AreEqual(reason, expense.History[0].Reason);
    }

    /// <summary>Pagamento cria registro consistente e nÃ£o pode ser repetido.</summary>
    [TestMethod]
    public void PaymentRecordsServerValuesAndRejectsDuplicates()
    {
        var expense = Expense(ExpenseState.Approved);
        ClaimsPrincipal finance = User("finance", Roles.Finance);
        ExpenseRules.Pay(expense, finance, _now);
        Assert.AreEqual(ExpenseState.Paid, expense.State);
        Assert.IsNotNull(expense.Payment);
        Assert.AreEqual("finance", expense.Payment.ActorId);
        Assert.AreEqual(_now, expense.Payment.PaidAt);
        Assert.AreEqual(expense.Amount, expense.Payment.Amount);
        Assert.AreEqual(expense.Id, expense.Payment.ExpenseId);
        Assert.AreEqual(409, Assert.Throws<BusinessException>(() => ExpenseRules.Pay(expense, finance, _now)).StatusCode);
        Assert.HasCount(1, expense.History);
    }

    /// <summary>ProprietÃ¡rio com Finance nÃ£o paga a prÃ³pria despesa.</summary>
    [TestMethod]
    public void OwnerCannotPay()
    {
        var expense = Expense(ExpenseState.Approved);
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.Pay(expense, User("owner", Roles.Employee, Roles.Finance), _now)).StatusCode);
        Assert.IsNull(expense.Payment);
        Assert.IsEmpty(expense.History);
    }

    /// <summary>Finance paga somente despesas aprovadas.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(4)]
    public void PaymentRejectsWrongState(int state)
    {
        Assert.AreEqual(409, Assert.Throws<BusinessException>(() => ExpenseRules.Pay(Expense((ExpenseState)state), User("finance", Roles.Finance), _now)).StatusCode);
    }

    /// <summary>Somente proprietÃ¡rio Employee edita ou envia Draft.</summary>
    [TestMethod]
    public void DraftOwnershipIsRequired()
    {
        var expense = Expense(ExpenseState.Draft);
        ExpenseRules.RequireOwnerDraft(expense, User("owner", Roles.Employee));
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.RequireOwnerDraft(expense, User("other", Roles.Employee))).StatusCode);
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.RequireOwnerDraft(expense, User("owner", Roles.Auditor))).StatusCode);
        expense.State = ExpenseState.Submitted;
        Assert.AreEqual(409, Assert.Throws<BusinessException>(() => ExpenseRules.RequireOwnerDraft(expense, User("owner", Roles.Employee))).StatusCode);
    }

    /// <summary>Filtro Employee isola proprietÃ¡rios; Admin nÃ£o possui acesso implÃ­cito.</summary>
    [TestMethod]
    public void EmployeeVisibilityAndAdminRestrictions()
    {
        var filter = ExpenseRules.Visibility(User("owner", Roles.Employee)).Compile();
        Assert.IsTrue(filter(Expense(ExpenseState.Paid)));
        var other = Expense(ExpenseState.Draft);
        other.OwnerId = "other";
        Assert.IsFalse(filter(other));
        Assert.AreEqual(403, Assert.Throws<BusinessException>(() => ExpenseRules.Visibility(User("admin", Roles.Admin))).StatusCode);
        Assert.IsTrue(ExpenseRules.Visibility(User("auditor", Roles.Auditor)).Compile()(other));
    }

    /// <summary>Consulta por perfis respeita o estado.</summary>
    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(1, true, false)]
    [DataRow(2, false, true)]
    [DataRow(3, false, false)]
    [DataRow(4, false, true)]
    public void ProfileVisibilityUsesState(int state, bool approver, bool finance)
    {
        var expense = Expense((ExpenseState)state);
        Assert.AreEqual(approver, ExpenseRules.Visibility(User("other", Roles.Approver)).Compile()(expense));
        Assert.AreEqual(finance, ExpenseRules.Visibility(User("other", Roles.Finance)).Compile()(expense));
    }

    /// <summary>Roles acumulam escopos sem perder isolamento contextual.</summary>
    [TestMethod]
    public void MultipleRolesUnionReadVisibility()
    {
        var expense = Expense(ExpenseState.Draft);
        var filter = ExpenseRules.Visibility(User("owner", Roles.Employee, Roles.Approver)).Compile();
        Assert.IsTrue(filter(expense));
        expense.OwnerId = "other";
        Assert.IsFalse(filter(expense));
        expense.State = ExpenseState.Submitted;
        Assert.IsTrue(filter(expense));
    }

    /// <summary>Limites de valor e data futura sÃ£o validados.</summary>
    [TestMethod]
    public void AmountAndDateValidation()
    {
        var input = new ExpenseRequest { Description = "Despesa de transporte", Amount = 0.01m, ExpenseDate = new DateOnly(2026, 10, 13), CategoryId = 1 };
        ExpenseRules.Validate(input, input.ExpenseDate);
        input.Amount = int.MaxValue;
        ExpenseRules.Validate(input, input.ExpenseDate);
        input.Amount = 0;
        Assert.AreEqual(400, Assert.Throws<BusinessException>(() => ExpenseRules.Validate(input, input.ExpenseDate)).StatusCode);
        input.Amount = (decimal)int.MaxValue + 0.01m;
        Assert.Throws<BusinessException>(() => ExpenseRules.Validate(input, input.ExpenseDate));
        input.Amount = 10;
        Assert.Throws<BusinessException>(() => ExpenseRules.Validate(input, input.ExpenseDate.AddDays(-1)));
        input.ExpenseDate = default;
        Assert.Throws<BusinessException>(() => ExpenseRules.Validate(input, new DateOnly(2026, 10, 13)));
    }

    /// <summary>DescriÃ§Ã£o vazia ou fora dos limites Ã© invÃ¡lida.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(9)]
    [DataRow(501)]
    public void DescriptionValidation(int length)
    {
        var input = new ExpenseRequest { Description = new string('x', length), Amount = 1, ExpenseDate = new DateOnly(2026, 10, 13), CategoryId = 1 };
        Assert.Throws<BusinessException>(() => ExpenseRules.Validate(input, input.ExpenseDate));
    }

    private static Expense Expense(ExpenseState state) => new() { OwnerId = "owner", State = state, Amount = 125.50m };

    private static ClaimsPrincipal User(string id, params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        foreach (string role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }
}

