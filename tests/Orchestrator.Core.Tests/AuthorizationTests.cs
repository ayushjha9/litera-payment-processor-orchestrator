using Orchestrator.Core.Models;

namespace Orchestrator.Core.Tests;

/// <summary>
/// Authorization is evaluated before approval, so an unauthorized role is refused even
/// while holding a valid approval.
/// </summary>
public sealed class AuthorizationTests
{
    private const string Action = "markVendorApproved";
    private const string ValidApprover = "compliance@tenant-b.example";

    private readonly WorkflowFixture _fixture = new();

    [Theory]
    [InlineData(Role.Viewer, "viewer@tenant-b.example")]
    [InlineData(Role.Analyst, "analyst@tenant-b.example")]
    public void Unauthorized_role_cannot_execute_even_with_a_valid_approval(Role role, string userId)
    {
        var result = _fixture.Run("tenant-b", userId, role, requestedAction: Action, approvedBy: ValidApprover);

        Assert.Equal(ActionStatus.BlockedUnauthorized, result.ActionStatus);
        Assert.Equal("pending", _fixture.VendorStatus("tenant-b"));
    }

    [Fact]
    public void Approver_role_can_execute_with_a_valid_approval()
    {
        var result = _fixture.Run(
            "tenant-b", "approver@tenant-b.example", Role.Approver,
            requestedAction: Action, approvedBy: ValidApprover);

        Assert.Equal(ActionStatus.Executed, result.ActionStatus);
        Assert.Equal("approved", _fixture.VendorStatus("tenant-b"));
    }

    /// <summary>
    /// The Python original rejected an unknown role string inside <c>run_workflow</c>. In the
    /// .NET port <see cref="Role"/> is a parsed enum by the time the engine sees it, so the
    /// rejection moves to the edge — where the string actually arrives. Proven here at the
    /// domain level, and over HTTP by the API test suite.
    /// </summary>
    [Fact]
    public void Unknown_role_string_has_no_valid_enum_representation()
    {
        Assert.False(Enum.TryParse<Role>("superadmin", ignoreCase: true, out _));
        Assert.False(Enum.IsDefined((Role)999));
    }
}
