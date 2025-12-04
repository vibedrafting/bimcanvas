using System.Collections.Generic;

namespace BIMCanvas.Core.Validation
{
    /// <summary>
    /// 泛型结果类型，表示成功或失败
    /// </summary>
    /// <typeparam name="T">成功时的值类型</typeparam>
    /// <typeparam name="TError">失败时的错误类型</typeparam>
    public readonly struct Result<T, TError>
    {
#nullable disable
        private readonly T _value;
        private readonly TError _error;
#nullable restore

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 是否失败
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// 成功时的值（失败时为 default）
        /// </summary>
        public T Value => _value;

        /// <summary>
        /// 失败时的错误（成功时为 default）
        /// </summary>
        public TError Error => _error;

        private Result(bool isSuccess, T value, TError error)
        {
            IsSuccess = isSuccess;
            _value = value;
            _error = error;
        }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static Result<T, TError> Success(T value)
        {
            return new Result<T, TError>(true, value, default!);
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static Result<T, TError> Failure(TError error)
        {
            return new Result<T, TError>(false, default!, error);
        }
    }

    /// <summary>
    /// 验证违规项
    /// </summary>
    public class Violation
    {
        /// <summary>
        /// 违规消息
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 违规代码（可选）
        /// </summary>
        public string? Code { get; }

        /// <summary>
        /// 相关对象 ID（可选）
        /// </summary>
        public string? ObjectId { get; }

        public Violation(string message, string? code = null, string? objectId = null)
        {
            Message = message;
            Code = code;
            ObjectId = objectId;
        }

        public override string ToString()
        {
            if (Code != null && ObjectId != null)
                return $"[{Code}] {Message} (Object: {ObjectId})";
            if (Code != null)
                return $"[{Code}] {Message}";
            return Message;
        }
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        private readonly List<Violation> _violations;

        /// <summary>
        /// 是否验证通过
        /// </summary>
        public bool IsValid => _violations.Count == 0;

        /// <summary>
        /// 违规列表
        /// </summary>
        public IReadOnlyList<Violation> Violations => _violations;

        private ValidationResult(List<Violation> violations)
        {
            _violations = violations;
        }

        /// <summary>
        /// 创建成功的验证结果
        /// </summary>
        public static ValidationResult Success()
        {
            return new ValidationResult(new List<Violation>());
        }

        /// <summary>
        /// 创建失败的验证结果
        /// </summary>
        public static ValidationResult Failure(List<Violation> violations)
        {
            return new ValidationResult(violations);
        }

        /// <summary>
        /// 创建失败的验证结果（单个违规）
        /// </summary>
        public static ValidationResult Failure(Violation violation)
        {
            return new ValidationResult(new List<Violation> { violation });
        }

        /// <summary>
        /// 创建失败的验证结果（简单消息）
        /// </summary>
        public static ValidationResult Failure(string message)
        {
            return Failure(new Violation(message));
        }
    }
}
