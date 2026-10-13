namespace Secucode.Api.Common;

public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public Validator Rule(bool ok, string field, string message)
    {
        if (!ok)
        {
            if (!_errors.TryGetValue(field, out var list))
                _errors[field] = list = [];
            list.Add(message);
        }
        return this;
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
            throw new ApiException(400, "Validation failed", "One or more fields are invalid.",
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
