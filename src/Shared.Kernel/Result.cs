namespace GreyGray.Shared.Kernel;

/// <summary>
/// 領域錯誤。<b>可預期</b>的業務失敗走這裡，不用例外——
/// 例外留給「不該發生」的狀況（違反不變式、基礎設施故障）。
/// </summary>
/// <param name="Code">穩定的機器可讀代碼，會出現在 RFC 9457 Problem Details 的 type 欄位。</param>
/// <param name="Message">給人看的訊息（繁體中文，客服可直接照念）。</param>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public readonly record struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result Failure(string code, string message) => new(false, new Error(code, message));
}

public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"讀取失敗結果的值：{Error.Code} {Error.Message}");

    public static Result<T> Success(T value) => new(true, value, Error.None);

    public static Result<T> Failure(Error error) => new(false, default, error);

    public static Result<T> Failure(string code, string message)
        => new(false, default, new Error(code, message));

    public static implicit operator Result<T>(T value) => Success(value);
}
