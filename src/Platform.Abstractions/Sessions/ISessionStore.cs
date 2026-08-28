using GreyGray.Shared.Kernel;

namespace GreyGray.Platform.Abstractions.Sessions;

public enum SessionSubjectKind
{
    Customer = 1,
    Staff = 2,
}

/// <summary>只存在伺服器端的 BFF session；cookie 內只有不透明 token。</summary>
public sealed record SessionRecord(
    string SubjectId,
    SessionSubjectKind SubjectKind,
    TenantId TenantId,
    string? Role,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public interface ISessionStore
{
    Task<string> CreateAsync(
        string subjectId,
        SessionSubjectKind subjectKind,
        TenantId tenantId,
        string? role,
        CancellationToken cancellationToken);

    Task<SessionRecord?> GetAsync(string token, CancellationToken cancellationToken);

    Task DeleteAsync(string token, CancellationToken cancellationToken);
}
