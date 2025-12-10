using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 事务的警告忽略等级
    /// </summary>
    public enum FailureLevel
    {
        /// <summary>
        /// 完全忽略所有警告和错误
        /// </summary>
        IgnoreWarningsAndErrors,  // 完全忽略所有警告和错误
        /// <summary>
        /// 记录警告并回滚事务
        /// </summary>
        LogWarningsAndRollback,   // 记录警告并回滚事务
        /// <summary>
        /// 记录警告并继续执行，遇到错误回滚事务
        /// </summary>
        LogWarningsAndContinueWithRollback,  // 记录警告并继续执行，遇到错误回滚事务
        /// <summary>
        /// 记录警告并继续执行，遇到错误抛出异常
        /// </summary>
        LogWarningsAndThrowException  // 记录警告并继续执行，遇到错误抛出异常
    }
    public static class TransactionHelper
    {
        /// <summary>
        /// 设置事务警告的处理方式
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="failureLevel"></param>
        public static void IgnoreFailure(this Transaction transaction, FailureLevel failureLevel = FailureLevel.IgnoreWarningsAndErrors)
        {
            // 获取事务的FailureHandlingOptions
            FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();

            // 根据不同的失败处理等级配置失败处理
            switch (failureLevel)
            {
                case FailureLevel.LogWarningsAndRollback:
                    // 记录警告并回滚事务
                    failureOptions.SetFailuresPreprocessor(new FailureHandlerLevel0());
                    failureOptions.SetClearAfterRollback(true); // 回滚时静默清除失败
                    break;
                case FailureLevel.LogWarningsAndContinueWithRollback:
                    // 记录警告并继续执行，遇到错误回滚事务
                    failureOptions.SetFailuresPreprocessor(new FailureHandlerLevel1());
                    failureOptions.SetClearAfterRollback(true); // 回滚时静默清除失败
                    break;
                case FailureLevel.LogWarningsAndThrowException:
                    // 记录警告并继续执行，遇到错误抛出异常
                    failureOptions.SetFailuresPreprocessor(new FailureHandlerLevel2());
                    break;
                case FailureLevel.IgnoreWarningsAndErrors:
                    // 完全忽略所有警告和错误
                    failureOptions.SetFailuresPreprocessor(new FailureHandlerLevel3());
                    failureOptions.SetClearAfterRollback(true); // 回滚时静默清除失败
                    failureOptions.SetDelayedMiniWarnings(true); // 延迟mini警告显示
                    failureOptions.SetForcedModalHandling(false); // 非模态处理
                    break;
                default:
                    // 默认情况，使用默认的处理方式
                    failureOptions.SetFailuresPreprocessor(new FailureHandlerLevel3());
                    failureOptions.SetClearAfterRollback(true);
                    failureOptions.SetDelayedMiniWarnings(true);
                    failureOptions.SetForcedModalHandling(false);
                    break;
            }

            // 将配置应用到事务
            transaction.SetFailureHandlingOptions(failureOptions);
        }
    }

    /// <summary>
    /// 等级 3：完全忽略所有警告和错误，静默处理
    /// </summary>
    public class FailureHandlerLevel3 : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            #region Step1 获取所有失败消息
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();

            // 如果没有失败，直接继续
            if (failureMessages.Count == 0)
                return FailureProcessingResult.Continue;
            #endregion

            #region Step2 静默处理每个失败消息
            bool hasProcessedFailures = false;

            foreach (FailureMessageAccessor failureMessage in failureMessages)
            {
                // 获取失败的严重程度
                FailureSeverity severity = failureMessage.GetSeverity();

                // 记录失败信息用于调试（静默记录，不弹窗）
                System.Diagnostics.Trace.WriteLine($"[FailureHandler-Silent] {severity}: {failureMessage.GetDescriptionText()}");

                if (severity == FailureSeverity.Warning)
                {
                    // 删除警告
                    failuresAccessor.DeleteWarning(failureMessage);
                    hasProcessedFailures = true;
                }
                else if (severity == FailureSeverity.Error)
                {
                    // 尝试解决错误
                    if (failureMessage.HasResolutions())
                    {
                        try
                        {
                            failuresAccessor.ResolveFailure(failureMessage);
                            hasProcessedFailures = true;
                            System.Diagnostics.Trace.WriteLine($"  -> Error resolved automatically");
                        }
                        catch
                        {
                            // 解决失败，记录但不抛出异常
                            System.Diagnostics.Trace.WriteLine($"  -> Failed to resolve error, will rollback silently");
                        }
                    }
                }
            }
            #endregion

            #region Step3 返回适当的处理结果
            // 关键：如果处理了任何失败，返回ProceedWithCommit让Revit重新处理
            // 这样已解决的失败会被移除，不会显示给用户
            if (hasProcessedFailures)
            {
                return FailureProcessingResult.ProceedWithCommit;
            }

            // 如果还有未解决的错误，静默回滚（配合SetClearAfterRollback确保不显示弹窗）
            var remainingErrors = failuresAccessor.GetFailureMessages(FailureSeverity.Error);
            if (remainingErrors.Count > 0)
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }

            // 没有失败或都已处理完成
            return FailureProcessingResult.Continue;
            #endregion
        }
    }

    /// <summary>
    /// 等级 2：记录警告并继续执行，遇到错误抛出异常
    /// </summary>
    public class FailureHandlerLevel2 : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            #region Step1 获取失败消息
            var failures = failuresAccessor.GetFailureMessages();
            bool hasProcessedFailures = false;
            #endregion

            #region Step2 处理失败消息
            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    // 记录警告但不显示弹窗
                    System.Diagnostics.Trace.WriteLine($"[Warning] {failure.GetDescriptionText()}");
                    failuresAccessor.DeleteWarning(failure);
                    hasProcessedFailures = true;
                }
                else if (failure.GetSeverity() == FailureSeverity.Error)
                {
                    // 记录错误并抛出异常
                    string errorMsg = failure.GetDescriptionText();
                    System.Diagnostics.Trace.WriteLine($"[Error] {errorMsg}");
                    throw new System.InvalidOperationException($"Critical error encountered: {errorMsg}");
                }
            }
            #endregion

            #region Step3 返回处理结果
            if (hasProcessedFailures)
            {
                return FailureProcessingResult.ProceedWithCommit;
            }
            return FailureProcessingResult.Continue;
            #endregion
        }
    }

    /// <summary>
    /// 等级 1：记录警告并继续执行，遇到错误静默回滚事务
    /// </summary>
    public class FailureHandlerLevel1 : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            #region Step1 获取失败消息
            var failures = failuresAccessor.GetFailureMessages();
            bool hasProcessedFailures = false;
            #endregion

            #region Step2 处理失败消息
            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    // 记录警告但不显示弹窗
                    System.Diagnostics.Trace.WriteLine($"[Warning] {failure.GetDescriptionText()}");
                    failuresAccessor.DeleteWarning(failure);
                    hasProcessedFailures = true;
                }
                else if (failure.GetSeverity() == FailureSeverity.Error)
                {
                    // 记录错误并静默回滚
                    System.Diagnostics.Trace.WriteLine($"[Error] {failure.GetDescriptionText()} - Transaction will rollback");
                    return FailureProcessingResult.ProceedWithRollBack;
                }
            }
            #endregion

            #region Step3 返回处理结果
            if (hasProcessedFailures)
            {
                return FailureProcessingResult.ProceedWithCommit;
            }
            return FailureProcessingResult.Continue;
            #endregion
        }
    }

    /// <summary>
    /// 等级 0：记录所有警告并回滚事务
    /// </summary>
    public class FailureHandlerLevel0 : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            #region Step1 获取失败消息并记录
            var failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                // 记录所有失败但不显示弹窗
                System.Diagnostics.Trace.WriteLine($"[{failure.GetSeverity()}] {failure.GetDescriptionText()}");
            }
            #endregion

            #region Step2 静默回滚事务
            // 直接回滚事务，撤销所有更改
            // 配合SetClearAfterRollback确保静默处理
            return FailureProcessingResult.ProceedWithRollBack;
            #endregion
        }
    }
}
