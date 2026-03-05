<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import type { ChatBubble, UserQuestion } from '../../types/agent'

const props = defineProps<{
  bubble: ChatBubble
}>()

const emit = defineEmits<{
  submit: [bubble: ChatBubble]
  cancel: [bubble: ChatBubble]
}>()

// 每个问题的选中状态: questionText -> Set<selectedLabel>
const selections = reactive<Record<string, Set<string>>>({})
// Other 输入框的展开状态和内容
const otherExpanded = reactive<Record<string, boolean>>({})
const otherText = reactive<Record<string, string>>({})

// 初始化
if (props.bubble.questions) {
  for (const q of props.bubble.questions) {
    selections[q.question] = new Set()
    otherExpanded[q.question] = false
    otherText[q.question] = ''
  }
}

const isSubmitted = computed(() => props.bubble.questionSubmitted)

const isSubmittable = computed(() => {
  if (!props.bubble.questions) return false
  return props.bubble.questions.every(q => {
    const set = selections[q.question]
    const hasOther = otherExpanded[q.question] && otherText[q.question].trim()
    return (set?.size ?? 0) > 0 || hasOther
  })
})

const toggleOption = (question: UserQuestion, label: string) => {
  if (isSubmitted.value) return
  const set = selections[question.question]
  if (question.multiSelect) {
    if (set.has(label)) set.delete(label)
    else set.add(label)
  } else {
    set.clear()
    otherExpanded[question.question] = false
    otherText[question.question] = ''
    set.add(label)
  }
}

const toggleOther = (question: UserQuestion) => {
  if (isSubmitted.value) return
  const key = question.question
  if (!question.multiSelect) {
    // 单选：选 Other 时清除其他
    selections[key].clear()
  }
  otherExpanded[key] = !otherExpanded[key]
  if (!otherExpanded[key]) {
    otherText[key] = ''
  }
}

const isOtherActive = (question: UserQuestion) => {
  return otherExpanded[question.question] && otherText[question.question].trim()
}

const buildAnswers = (): Record<string, string> => {
  const answers: Record<string, string> = {}
  for (const q of (props.bubble.questions || [])) {
    const parts: string[] = [...(selections[q.question] || [])]
    const other = otherText[q.question]?.trim()
    if (other) parts.push(other)
    answers[q.question] = parts.join(', ')
  }
  return answers
}

const handleSubmit = () => {
  if (!isSubmittable.value || isSubmitted.value) return
  props.bubble.questionAnswers = buildAnswers()
  emit('submit', props.bubble)
}

const handleCancel = () => {
  if (isSubmitted.value) return
  emit('cancel', props.bubble)
}

// 提交后的摘要
const answerSummary = computed(() => {
  const answers = props.bubble.questionAnswers || {}
  return Object.values(answers).filter(v => v).join(' / ')
})
</script>

<template>
  <div class="question-bubble" :class="{ submitted: isSubmitted }">
    <!-- 等待回答状态 -->
    <template v-if="!isSubmitted">
      <div class="question-title">
        <span class="question-icon">&#128172;</span>
        <span>需要你的决策</span>
      </div>

      <div v-for="q in bubble.questions" :key="q.question" class="question-item">
        <div class="question-text">
          <span class="header-chip">{{ q.header }}</span>
          <span>{{ q.question }}</span>
          <span v-if="q.multiSelect" class="multi-hint">(可多选)</span>
        </div>

        <div class="options-grid">
          <button
            v-for="opt in q.options"
            :key="opt.label"
            class="option-card"
            :class="{ selected: selections[q.question]?.has(opt.label) }"
            @click="toggleOption(q, opt.label)"
          >
            <span class="option-label">{{ opt.label }}</span>
            <span v-if="opt.description" class="option-desc">{{ opt.description }}</span>
          </button>

          <!-- Other 选项 -->
          <button
            class="option-card option-other"
            :class="{ selected: isOtherActive(q) }"
            @click="toggleOther(q)"
          >
            <span class="option-label">其他...</span>
          </button>
        </div>

        <!-- Other 输入框 -->
        <div v-if="otherExpanded[q.question]" class="other-input-wrapper">
          <textarea
            v-model="otherText[q.question]"
            class="other-input"
            placeholder="输入自定义答案..."
            rows="2"
          />
        </div>
      </div>

      <div class="question-actions">
        <button class="btn-skip" @click="handleCancel">跳过</button>
        <button class="btn-submit" :disabled="!isSubmittable" @click="handleSubmit">确认选择</button>
      </div>
    </template>

    <!-- 已提交摘要 -->
    <template v-else>
      <div class="question-result">
        <span v-if="answerSummary" class="result-text">已选择: {{ answerSummary }}</span>
        <span v-else class="result-skipped">已跳过</span>
      </div>
    </template>
  </div>
</template>

<style scoped lang="scss">
.question-bubble {
  --card-bg: rgba(20, 20, 22, 0.4);
  --card-border: rgba(255, 255, 255, 0.06);
  --accent: #818cf8;  // indigo-400
  --accent-rgb: 129, 140, 248;

  background: var(--card-bg);
  border: 1px solid rgba(var(--accent-rgb), 0.25);
  border-radius: 8px;
  margin: 6px 0;
  padding: 12px;
  font-family: 'Inter', system-ui, sans-serif;

  &.submitted {
    padding: 8px 12px;
    border-color: var(--card-border);
  }
}

.question-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent);
  margin-bottom: 12px;
}

.question-icon {
  font-size: 14px;
}

.question-item {
  margin-bottom: 14px;

  &:last-of-type {
    margin-bottom: 10px;
  }
}

.question-text {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: rgba(255, 255, 255, 0.85);
  margin-bottom: 8px;
  flex-wrap: wrap;
}

.header-chip {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 4px;
  background: rgba(var(--accent-rgb), 0.15);
  color: var(--accent);
  font-size: 10px;
  font-weight: 600;
  flex-shrink: 0;
}

.multi-hint {
  color: rgba(255, 255, 255, 0.4);
  font-size: 11px;
}

.options-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.option-card {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  cursor: pointer;
  transition: all 0.15s ease;
  text-align: left;
  color: rgba(255, 255, 255, 0.8);

  &:hover {
    border-color: rgba(var(--accent-rgb), 0.3);
    background: rgba(var(--accent-rgb), 0.05);
  }

  &.selected {
    border-color: var(--accent);
    background: rgba(var(--accent-rgb), 0.12);
    color: #fff;
  }
}

.option-label {
  font-size: 12px;
  font-weight: 500;
}

.option-desc {
  font-size: 10px;
  color: rgba(255, 255, 255, 0.45);
  line-height: 1.3;
}

.option-other {
  border-style: dashed;
}

.other-input-wrapper {
  margin-top: 6px;
}

.other-input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  color: rgba(255, 255, 255, 0.85);
  font-size: 12px;
  font-family: inherit;
  resize: vertical;
  outline: none;
  box-sizing: border-box;

  &:focus {
    border-color: rgba(var(--accent-rgb), 0.4);
  }

  &::placeholder {
    color: rgba(255, 255, 255, 0.3);
  }
}

.question-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 4px;
}

.btn-skip {
  padding: 4px 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  background: transparent;
  color: rgba(255, 255, 255, 0.5);
  font-size: 11px;
  cursor: pointer;

  &:hover {
    color: rgba(255, 255, 255, 0.8);
    border-color: rgba(255, 255, 255, 0.2);
  }
}

.btn-submit {
  padding: 4px 14px;
  border: none;
  border-radius: 6px;
  background: var(--accent);
  color: #fff;
  font-size: 11px;
  font-weight: 500;
  cursor: pointer;

  &:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }

  &:not(:disabled):hover {
    filter: brightness(1.1);
  }
}

.question-result {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
}

.result-text {
  color: rgba(255, 255, 255, 0.6);
}

.result-skipped {
  color: rgba(255, 255, 255, 0.35);
  font-style: italic;
}
</style>
