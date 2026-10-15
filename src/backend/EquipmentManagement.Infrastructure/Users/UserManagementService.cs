using System.Data;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Users;

/// <summary>
/// Administration-only internal-user management (Phase 1 scope). Encapsulates every write-side business rule
/// (duplicate email, supported role, HR address requirement, the last-Administration
/// safeguard) so <c>UsersController</c> stays a thin HTTP adapter: authorization is not a
/// substitute for domain rules.
/// </summary>
public sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    AuditService auditService)
{
    public async Task<PagedResult<UserRecord>> ListAsync(
        UserListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(u =>
                EF.Functions.ILike(u.FirstName, $"%{filter.Search}%") ||
                EF.Functions.ILike(u.LastName, $"%{filter.Search}%") ||
                EF.Functions.ILike(u.Email!, $"%{filter.Search}%"));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var role = await roleManager.FindByNameAsync(filter.Role);
            var roleId = role?.Id ?? Guid.Empty;
            query = query.Where(u => dbContext.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var projected = await (
                from u in query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                join ur in dbContext.UserRoles on u.Id equals ur.UserId
                join r in dbContext.Roles on ur.RoleId equals r.Id
                select new { User = u, RoleName = r.Name! })
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        var items = projected.Select(x => ToRecord(x.User, x.RoleName)).ToList();

        return new PagedResult<UserRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<UserRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return null;
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        return ToRecord(user, role);
    }

    public async Task<ServiceResult<CreatedUser>> CreateAsync(
        CreateUserData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (!UserProfileRules.IsSupportedRole(data.Role))
        {
            return ServiceResult.Fail<CreatedUser>(ServiceErrorCode.UnsupportedRole, $"'{data.Role}' não é uma função suportada.");
        }

        if (UserProfileRules.RequiresAddress(data.Role) && UserProfileRules.IsAddressMissing(data.Address))
        {
            return ServiceResult.Fail<CreatedUser>(ServiceErrorCode.AddressRequiredForRole, "A morada é obrigatória para utilizadores de RH.");
        }

        if (await userManager.FindByEmailAsync(data.Email) is not null)
        {
            return ServiceResult.Fail<CreatedUser>(ServiceErrorCode.DuplicateEmail, "Já existe um utilizador com este e-mail.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = data.Email,
            Email = data.Email,
            EmailConfirmed = true,
            PhoneNumber = UserProfileRules.NormalizeOptionalPhone(data.PhoneNumber),
            FirstName = data.FirstName,
            LastName = data.LastName,
            Address = data.Address,
            IsActive = true,
            MustChangePassword = true,
        };

        var temporaryPassword = TemporaryPasswordGenerator.Generate();

        // UserManager's store and AuditService share this request's DbContext,
        // so the user, its role and the audit row commit together (ADR-076) —
        // never a user without a role, or an account with no record of who created it.
        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, temporaryPassword);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            // The FindByEmailAsync check above is a TOCTOU pre-check, not a
            // guarantee: two concurrent requests for the same email can both
            // pass it. UserName is always set to Email (see below), so
            // AspNetUsers' unique index on NormalizedUserName is the real
            // DB-level guarantee — Identity's own UserStore does not catch
            // this exception, so without this it would surface as an
            // unhandled 500 instead of the expected duplicate-email conflict.
            return ServiceResult.Fail<CreatedUser>(ServiceErrorCode.DuplicateEmail, "Já existe um utilizador com este e-mail.");
        }

        if (!createResult.Succeeded)
        {
            // Identity's own UserValidator re-checks uniqueness (another TOCTOU
            // read, independent of the pre-check above and of the DB-level catch
            // below) and fails this way — without throwing — when IT loses the
            // same race. Without this, that ordinary outcome would surface as a
            // generic 400 instead of the expected duplicate-email conflict.
            return ServiceResult.Fail<CreatedUser>(
                IsDuplicateFailure(createResult) ? ServiceErrorCode.DuplicateEmail : ServiceErrorCode.Validation,
                DescribeErrors(createResult));
        }

        await userManager.AddToRoleAsync(user, data.Role);

        // PhoneNumber/Address are never written as values here, only whether
        // they were provided. FirstName/LastName/Email/Role stay as values on
        // creation since all four are already plainly visible in the Users list
        // itself, unlike contact PII. Later edits (User.Updated,
        // User.ProfileUpdated) record changed field names only.
        await auditService.RecordAsync(new AuditEntry(
            AuditActions.User.Created,
            EntityType: "User",
            EntityId: user.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new
            {
                data.FirstName,
                data.LastName,
                data.Email,
                data.Role,
                HasPhoneNumber = !string.IsNullOrWhiteSpace(data.PhoneNumber),
                HasAddress = !string.IsNullOrWhiteSpace(data.Address),
            }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(new CreatedUser(ToRecord(user, data.Role), temporaryPassword));
    }

    public async Task<ServiceResult<UserRecord>> UpdateAsync(
        Guid id, UpdateUserData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.NotFound, "Utilizador não encontrado.");
        }

        if (data.ExpectedConcurrencyStamp is { } expected && !string.Equals(expected, user.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.Concurrency, "O utilizador foi alterado por outra pessoa. Recarregue e tente novamente.");
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        if (ValidateAddressForRole(role, data.Address) is { } addressFailure)
        {
            return addressFailure;
        }

        var phoneNumber = UserProfileRules.NormalizeOptionalPhone(data.PhoneNumber);

        // Field NAMES only — never old/new values (names, phone and address are
        // personal data); same shape as User.ProfileUpdated.
        var changedFields = new List<string>();
        AddNameIfChanged(changedFields, "firstName", user.FirstName, data.FirstName);
        AddNameIfChanged(changedFields, "lastName", user.LastName, data.LastName);
        AddNameIfChanged(changedFields, "phoneNumber", user.PhoneNumber, phoneNumber);
        AddNameIfChanged(changedFields, "address", user.Address, data.Address);

        user.FirstName = data.FirstName;
        user.LastName = data.LastName;
        user.PhoneNumber = phoneNumber;
        user.Address = data.Address;

        // Same single-transaction shape as UpdateOwnProfileAsync below: a failed
        // audit write can't leave a persisted edit behind a 500 (ADR-076).
        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ServiceResult.Fail<UserRecord>(
                IsConcurrencyFailure(updateResult) ? ServiceErrorCode.Concurrency : ServiceErrorCode.Validation,
                DescribeErrors(updateResult));
        }

        if (changedFields.Count > 0)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.User.Updated,
                EntityType: "User",
                EntityId: user.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                Metadata: new { changedFields }), cancellationToken);
        }

        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(user, role));
    }

    /// <summary>
    /// Self-service edit of the signed-in user's own name/phone/address (ADR-073).
    /// The same rules as <see cref="UpdateAsync"/> apply (HR address requirement
    /// on the user's current role), but: the target is always
    /// <paramref name="userId"/> — the caller passes the authenticated principal's
    /// id, never a client-supplied one; the audit entry records changed field
    /// NAMES only, never values; and the update and its audit row commit in one
    /// transaction, so a failed audit write can't leave a persisted edit behind a
    /// 500. Email, role, activation, MustChangePassword and the security stamp are
    /// unreachable from here, and the security stamp is deliberately not bumped —
    /// none of these fields feed the auth cookie's claims, so the session stays valid.
    /// </summary>
    public async Task<ServiceResult<UserRecord>> UpdateOwnProfileAsync(
        Guid userId, UpdateOwnProfileData data, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.NotFound, "Utilizador não encontrado.");
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        if (ValidateAddressForRole(role, data.Address) is { } addressFailure)
        {
            return addressFailure;
        }

        var phoneNumber = UserProfileRules.NormalizeOptionalPhone(data.PhoneNumber);

        var changedFields = new List<string>();
        AddNameIfChanged(changedFields, "firstName", user.FirstName, data.FirstName);
        AddNameIfChanged(changedFields, "lastName", user.LastName, data.LastName);
        AddNameIfChanged(changedFields, "phoneNumber", user.PhoneNumber, phoneNumber);
        AddNameIfChanged(changedFields, "address", user.Address, data.Address);

        if (changedFields.Count == 0)
        {
            return ServiceResult.Ok(ToRecord(user, role));
        }

        // Snapshot before applying the edit: the audit row names who acted, as
        // they were known when they acted.
        var actorDisplayName = $"{user.FirstName} {user.LastName}";

        user.FirstName = data.FirstName;
        user.LastName = data.LastName;
        user.PhoneNumber = phoneNumber;
        user.Address = data.Address;

        // UserManager's store and AuditService share this request's DbContext, so
        // both SaveChanges calls enlist in this one transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ServiceResult.Fail<UserRecord>(
                IsConcurrencyFailure(updateResult) ? ServiceErrorCode.Concurrency : ServiceErrorCode.Validation,
                DescribeErrors(updateResult));
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.User.ProfileUpdated,
            EntityType: "User",
            EntityId: user.Id.ToString(),
            ActorUserId: user.Id,
            ActorDisplayNameSnapshot: actorDisplayName,
            Metadata: new { changedFields }), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(user, role));
    }

    public async Task<ServiceResult<UserRecord>> ChangeRoleAsync(
        Guid id, ChangeRoleData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (!UserProfileRules.IsSupportedRole(data.Role))
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.UnsupportedRole, $"'{data.Role}' não é uma função suportada.");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.NotFound, "Utilizador não encontrado.");
        }

        if (data.ExpectedConcurrencyStamp is { } expected && !string.Equals(expected, user.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.Concurrency, "O utilizador foi alterado por outra pessoa. Recarregue e tente novamente.");
        }

        var currentRole = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        if (string.Equals(currentRole, data.Role, StringComparison.Ordinal))
        {
            return ServiceResult.Ok(ToRecord(user, currentRole));
        }

        // The last-Administration safeguard is a safety invariant (avoiding an
        // irrecoverable lockout) and is checked before the address-completeness
        // rule below on purpose: a caller should never be able to tell whether
        // removing the last admin would have "only" failed on a data-quality
        // issue, and the more severe failure should always win.
        var isRemovingLastAdministrator = string.Equals(currentRole, Roles.Administration, StringComparison.Ordinal);

        // Always a transaction (ADR-076): the role swap is two UserManager
        // writes plus a security-stamp write plus the audit row, and must never
        // half-commit (e.g. a user left with no role, or a change with no audit).
        await using var transaction = isRemovingLastAdministrator
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (isRemovingLastAdministrator && !await CanRemoveAdministrationAccessAsync(user.Id, cancellationToken))
            {
                return ServiceResult.Fail<UserRecord>(
                    ServiceErrorCode.LastAdministrator,
                    "Este é o último utilizador ativo da Administração; atribua primeiro a função de Administração a outro utilizador.");
            }

            if (UserProfileRules.RequiresAddress(data.Role) && UserProfileRules.IsAddressMissing(user.Address))
            {
                return ServiceResult.Fail<UserRecord>(
                    ServiceErrorCode.AddressRequiredForRole,
                    "Este utilizador não tem morada registada; indique uma antes de lhe atribuir a função de RH.");
            }

            await userManager.RemoveFromRoleAsync(user, currentRole);
            await userManager.AddToRoleAsync(user, data.Role);

            // A signed-in session's permission claims come from the role assigned
            // at login time (see IdentitySeeder / ADR-005); bumping the security
            // stamp forces ActiveUserCookieValidator to reject the now-stale
            // session on its next request, so a role change takes effect promptly
            // instead of only at the user's next login.
            await userManager.UpdateSecurityStampAsync(user);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.User.RoleChanged,
                EntityType: "User",
                EntityId: user.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { Role = currentRole },
                NewValues: new { Role = data.Role }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<UserRecord>(
                ServiceErrorCode.Concurrency, "Está em curso outra alteração administrativa; tente novamente.");
        }

        return ServiceResult.Ok(ToRecord(user, data.Role));
    }

    public async Task<ServiceResult<UserRecord>> SetActiveAsync(
        Guid id, bool active, ActivationChangeData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.NotFound, "Utilizador não encontrado.");
        }

        if (data.ExpectedConcurrencyStamp is { } expected && !string.Equals(expected, user.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return ServiceResult.Fail<UserRecord>(ServiceErrorCode.Concurrency, "O utilizador foi alterado por outra pessoa. Recarregue e tente novamente.");
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

        if (user.IsActive == active)
        {
            return ServiceResult.Ok(ToRecord(user, role));
        }

        var isDeactivatingAdministrator = !active && string.Equals(role, Roles.Administration, StringComparison.Ordinal);

        // Always a transaction, so the activation change and its audit row
        // commit together (ADR-076).
        await using var transaction = isDeactivatingAdministrator
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (isDeactivatingAdministrator && !await CanRemoveAdministrationAccessAsync(user.Id, cancellationToken))
            {
                return ServiceResult.Fail<UserRecord>(
                    ServiceErrorCode.LastAdministrator,
                    "Este é o último utilizador ativo da Administração; atribua a função de Administração a outro utilizador antes de o desativar.");
            }

            user.IsActive = active;
            await userManager.UpdateAsync(user);

            if (!active)
            {
                // Belt-and-braces: IsActive alone already fails
                // ActiveUserCookieValidator, but bumping the stamp too closes any
                // edge case and matches the role-change behavior above.
                await userManager.UpdateSecurityStampAsync(user);
            }

            await auditService.RecordAsync(new AuditEntry(
                active ? AuditActions.User.Activated : AuditActions.User.Deactivated,
                EntityType: "User",
                EntityId: user.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<UserRecord>(
                ServiceErrorCode.Concurrency, "Está em curso outra alteração administrativa; tente novamente.");
        }

        return ServiceResult.Ok(ToRecord(user, role));
    }

    public async Task<ServiceResult<string>> InitiateAdminPasswordResetAsync(
        Guid id, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceResult.Fail<string>(ServiceErrorCode.NotFound, "Utilizador não encontrado.");
        }

        var temporaryPassword = TemporaryPasswordGenerator.Generate();

        // The new password, the forced-change flag and the audit row commit
        // together (ADR-076) — never a reset password without MustChangePassword.
        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!resetResult.Succeeded)
        {
            return ServiceResult.Fail<string>(ServiceErrorCode.Validation, DescribeErrors(resetResult));
        }

        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Auth.AdminPasswordResetRequested,
            EntityType: "User",
            EntityId: user.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(temporaryPassword);
    }

    private async Task<bool> CanRemoveAdministrationAccessAsync(Guid excludingUserId, CancellationToken cancellationToken)
    {
        var adminRole = await roleManager.FindByNameAsync(Roles.Administration);
        if (adminRole is null)
        {
            return true;
        }

        var otherActiveAdministrators = await dbContext.Users
            .Where(u => u.IsActive && u.Id != excludingUserId)
            .Where(u => dbContext.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRole.Id))
            .CountAsync(cancellationToken);

        return AdministrationSafeguard.CanRemoveAdministrationAccess(otherActiveAdministrators);
    }

    private static void AddNameIfChanged(List<string> changedFields, string field, string? oldValue, string? newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            changedFields.Add(field);
        }
    }

    /// <summary>The HR address requirement, checked against the user's CURRENT role (docs/PROJECT_SPEC.md, section 5).</summary>
    private static ServiceResult<UserRecord>? ValidateAddressForRole(string role, string? address) =>
        UserProfileRules.RequiresAddress(role) && UserProfileRules.IsAddressMissing(address)
            ? ServiceResult.Fail<UserRecord>(ServiceErrorCode.AddressRequiredForRole, "A morada é obrigatória para utilizadores de RH.")
            : null;

    private static bool IsConcurrencyFailure(IdentityResult result) =>
        result.Errors.Any(e => string.Equals(e.Code, "ConcurrencyFailure", StringComparison.Ordinal));

    private static bool IsDuplicateFailure(IdentityResult result) =>
        result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");

    private static string DescribeErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static UserRecord ToRecord(ApplicationUser user, string role) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email ?? string.Empty,
        user.PhoneNumber,
        user.Address,
        role,
        user.IsActive,
        user.MustChangePassword,
        user.ConcurrencyStamp ?? string.Empty);
}
