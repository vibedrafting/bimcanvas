<script setup lang="ts">
import { computed } from 'vue';
import MarkdownIt from 'markdown-it';

const props = defineProps<{
  content: string;
}>();

const md = new MarkdownIt({
  html: false,
  linkify: true,
  breaks: true,
  typographer: true
});

const renderedContent = computed(() => {
  if (!props.content) return '';
  return md.render(props.content);
});
</script>

<template>
  <div class="markdown-content" v-html="renderedContent"></div>
</template>

<style lang="scss">
/* Note: Not scoped because v-html content is not affected by scoped styles */
.markdown-content {
  font-size: inherit;
  line-height: 1.25; /* Tighter line height */
  color: inherit;

  /* Paragraphs */
  p {
    margin-top: 0;
    margin-bottom: 0.2em; /* Minimal margin */
    &:last-child {
      margin-bottom: 0;
    }
  }

  /* Lists */
  ul, ol {
    margin-top: 0;
    margin-bottom: 0.2em; /* Minimal margin */
    padding-left: 1.2em;
    
    li {
      margin-bottom: 0; /* No extra margin between items */
    }
  }

  /* Headings */
  h1, h2, h3, h4, h5, h6 {
    font-weight: 600;
    margin-top: 0.6em;
    margin-bottom: 0.3em;
    line-height: 1.2;
  }
  
  h1 { font-size: 1.4em; }
  h2 { font-size: 1.2em; }
  h3 { font-size: 1.1em; }

  /* Code Blocks */
  pre {
    background: rgba(0, 0, 0, 0.2);
    padding: 0.8em;
    border-radius: 6px;
    overflow-x: auto;
    margin-bottom: 0.8em;
    
    code {
      background: transparent;
      padding: 0;
      border-radius: 0;
      color: inherit;
    }
  }

  /* Inline Code */
  code {
    background: rgba(255, 255, 255, 0.1);
    padding: 0.2em 0.4em;
    border-radius: 4px;
    font-family: 'SF Mono', 'Roboto Mono', monospace;
    font-size: 0.9em;
  }

  /* Blockquotes */
  blockquote {
    border-left: 3px solid var(--accent-primary);
    margin: 0 0 0.8em 0;
    padding-left: 0.8em;
    opacity: 0.8;
  }

  /* Links */
  a {
    color: var(--accent-primary);
    text-decoration: none;
    &:hover {
      text-decoration: underline;
    }
  }
}
</style>
