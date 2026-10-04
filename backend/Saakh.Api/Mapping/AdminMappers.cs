using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Api.Mapping;

/// <summary>
/// Entity to DTO mapping for evidence documents, the admin action log and
/// notifications — the shapes behind the moderation surfaces.
/// </summary>
public static class AdminMappers
{
    /// <summary>
    /// Note what is absent: the storage key never leaves the server. The Admin
    /// console reaches a document through its id, so a storage path is never
    /// exposed to a client that could then guess at its neighbours.
    /// </summary>
    public static EvidenceDocumentDto ToDto(this EvidenceDocument document) =>
        new(
            document.Id,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            document.DocumentType,
            document.SubmittedAt,
            document.Decision,
            document.RejectionReason,
            document.ReviewedAt);

    public static AdminActionLogDto ToDto(this AdminActionLog entry) =>
        new(
            entry.Id,
            entry.AdminId,
            entry.Admin?.Name ?? string.Empty,
            entry.TargetProfileId,
            entry.TargetProfile?.Name ?? string.Empty,
            entry.ActionType,
            entry.SuspensionDuration,
            entry.Notes,
            entry.OccurredAt);

    public static NotificationDto ToDto(this Notification notification) =>
        new(
            notification.Id,
            notification.Kind,
            notification.Title,
            notification.Body,
            notification.Link,
            notification.IsRead,
            notification.CreatedAt);
}
