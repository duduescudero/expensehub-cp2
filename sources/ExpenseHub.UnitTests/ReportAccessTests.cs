using System;
using System.Linq;
using System.Security.Claims;
using ExpenseHub.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Permissões e propriedade sem banco ou rede.</summary>
[TestClass]
public sealed class ReportAccessTests
{
    /// <summary>A matriz não concede privilégios implícitos.</summary>
    [TestMethod]
    [DataRow("Employee", "Mine", true)]
    [DataRow("Employee", "Global", false)]
    [DataRow("Auditor", "Mine", false)]
    [DataRow("Auditor", "Global", true)]
    [DataRow("Employee,Auditor", "Mine", true)]
    [DataRow("Employee,Auditor", "Global", true)]
    [DataRow("Admin", "Mine", false)]
    [DataRow("Admin", "Global", false)]
    [DataRow("Approver", "Mine", false)]
    [DataRow("Finance", "Global", false)]
    public void ScopeMatchesRoles(string roles, string scope, bool allowed)
    {
        ClaimsPrincipal user = User("owner", roles);
        if (allowed)
        {
            Assert.AreEqual("owner", ReportRules.Authorize(user, scope));
        }
        else
        {
            Assert.AreEqual(403, Assert.Throws<ReportException>(() => ReportRules.Authorize(user, scope)).StatusCode);
        }
    }

    /// <summary>Usuário anônimo não pode solicitar relatórios.</summary>
    [TestMethod]
    public void AnonymousIsRejected() => Assert.AreEqual(401, Assert.Throws<ReportException>(() => ReportRules.Authorize(new ClaimsPrincipal(), "Mine")).StatusCode);

    /// <summary>A ausência do claim de identidade não cria um proprietário vazio.</summary>
    [TestMethod]
    public void IdentityWithoutIdentifierIsRejected()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Employee") }, "test"));
        Assert.AreEqual(401, Assert.Throws<ReportException>(() => ReportRules.Authorize(user, "Mine")).StatusCode);
    }

    /// <summary>Ausência e propriedade alheia não expõem resultados.</summary>
    [TestMethod]
    public void OwnershipDoesNotDiscloseForeignReports()
    {
        var report = new ReportRecord { OwnerId = "alice" };
        ReportRules.RequireOwner(report, "alice");
        Assert.AreEqual(404, Assert.Throws<ReportException>(() => ReportRules.RequireOwner(report, "bob")).StatusCode);
        Assert.AreEqual(404, Assert.Throws<ReportException>(() => ReportRules.RequireOwner(null, "alice")).StatusCode);
    }

    internal static ClaimsPrincipal User(string id, string roles) => new(new ClaimsIdentity(roles.Split(',').Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.NameIdentifier, id)), "test"));
}
