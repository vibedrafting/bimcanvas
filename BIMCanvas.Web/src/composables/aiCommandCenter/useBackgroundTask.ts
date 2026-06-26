import type { Ref } from 'vue'
import type { BackgroundTaskRecord } from '../../types/agent'
import type { ChatWindow } from '../../types/aiCommandCenter'
import { getBackgroundTaskService } from '../../services/BackgroundTaskService'
import { useWorkflowProgress } from './useWorkflowProgress'

const workflowProgress = useWorkflowProgress()

interface BackgroundTaskOptions {
  agentApiBase: string
  windows: Ref<ChatWindow[]>
  scrollToBottom: (options?: { windowId?: string }) => void
  /** P1：后台总结回合开始（建一条 streaming 气泡） */
  beginBackgroundTurn: (windowId: string | null | undefined, turnId: string, timestamp?: number) => void
  /** P1：后台总结回合逐 envelope 增量渲染（治本：用户实时可见） */
  applyBackgroundTurnChunk: (windowId: string | null | undefined, turnId: string, envelope: Record<string, unknown>) => void
  /** P1：后台总结回合收口（结束 streaming 态） */
  finalizeBackgroundTurn: (turnId: string, timestamp?: number) => boolean
  /** P1：该 turnId 是否已有 live 消息（完成事件去重用） */
  hasBackgroundLiveTurn: (turnId: string) => boolean
}

export const useBackgroundTask = (options: BackgroundTaskOptions) => {
  // 去重：仅防同一完成事件被重复实时投递。
  const seenKeys = new Set<string>()

  const handleCompleted = (record: BackgroundTaskRecord) => {
    if (record.taskId) {
      const key = `${record.sessionId ?? ''}:${record.taskId}`
      if (seenKeys.has(key)) {
        return
      }
      seenKeys.add(key)
      // 普通后台 Task 收口：标完成态保留条目（Task 页卡片显示）；对 workflow 的 taskId 调用无害（集合里本来没有）
      workflowProgress.completeBackgroundTask(
        record.taskId,
        record.status === 'failed' ? 'failed' : 'completed'
      )
    }

    // ① Task 面板收口（始终）——携带 sdkSessionId 供 tier C 拉 transcript。
    workflowProgress.onWorkflowCompleted({
      taskId: record.taskId,
      status: record.status,
      sdkSessionId: record.sdkSessionId
    })

    // ② Chat：归一后 completed 不再渲染气泡。总结内容在线由 live 流式（turn_started/turn_chunk）渲染，
    // 刷新由 history 重建；completed 只负责落盘（Agent 端）+ 收口 live streaming 态 + Task 面板收口。
    const ts = record.timestamp ? Date.parse(record.timestamp) : Date.now()
    const stamp = isNaN(ts) ? Date.now() : ts
    options.finalizeBackgroundTurn(`bgtask:${record.taskId}`, stamp)
  }

  const startListening = () => {
    getBackgroundTaskService(options.agentApiBase).startListening({
      onCompleted: handleCompleted,
      onProgress: (record) => {
        // 后台任务实时进度：isWorkflow=false 在 onWorkflowProgress 内分流进普通任务集合，
        // 其余 → Task 页 workflow 视图（detach 后唯一实时来源）
        workflowProgress.onWorkflowProgress({
          taskId: record.taskId,
          isWorkflow: record.isWorkflow,
          sdkSessionId: record.sdkSessionId,
          description: record.description,
          lastToolName: record.lastToolName,
          // 归属链字段必须透传——此处曾手工重组 record 丢掉新字段，
          // 导致分组/详情链路整体失效（2026-06-12 实测）
          toolUseId: record.toolUseId,
          ownerKind: record.ownerKind,
          ownerId: record.ownerId,
          taskKind: record.taskKind,
          usage: record.usage
        })
      },
      onPhases: (record) => {
        // 后台 Workflow 阶段预声明 → 运行态立即渲染全部阶段（不依赖脆弱的脚本文件读取）
        workflowProgress.onWorkflowPhases({
          taskId: record.taskId,
          sdkSessionId: record.sdkSessionId,
          workflowName: record.workflowName,
          phases: record.phases
        })
      },
      // P1 live 流式：后台总结回合开始 → 建一条 streaming 气泡（治本：用户即刻看到回合在跑）
      onTurnStarted: (record) => {
        options.beginBackgroundTurn(
          record.windowId,
          record.turnId,
          record.timestamp ? Date.parse(record.timestamp) : undefined
        )
      },
      // P1 live 流式：后台总结回合逐 envelope → 增量 apply 到该 turnId 的气泡
      onTurnChunk: (record) => {
        options.applyBackgroundTurnChunk(record.windowId, record.turnId, record.envelope)
      }
    })
  }

  const stopListening = () => {
    getBackgroundTaskService(options.agentApiBase).stopListening()
  }

  return { startListening, stopListening }
}
