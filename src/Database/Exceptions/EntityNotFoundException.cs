namespace SiteChecker.Database.Exceptions;

public class EntityNotFoundException<T>(int id)
    : Exception($"Entity '{nameof(T)}' with ID {id} not found.")
    where T : class
{ }
