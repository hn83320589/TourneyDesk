namespace TourneyDesk.Core.Domain;

/// <summary>違反賽事規則；訊息直接呈現給使用者，因此一律為可讀的中文。</summary>
public sealed class DomainException(string message) : Exception(message);
