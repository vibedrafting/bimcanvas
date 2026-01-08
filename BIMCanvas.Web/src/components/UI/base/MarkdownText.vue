<script setup lang="ts">
import MarkdownRender from 'markstream-vue'

defineProps<{
  content: string
}>()
</script>

<template>
  <MarkdownRender 
    :content="content" 
    :is-dark="true"
    :max-live-nodes="0"
  />
</template>
