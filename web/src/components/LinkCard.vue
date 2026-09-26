<script setup lang="ts">
import { computed } from 'vue'
import { toast } from 'vue-sonner'
import { api, type CollectionItem, type CollectResult } from '@/api/client'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

const props = defineProps<{ item: CollectResult | CollectionItem; showDelete?: boolean }>()
const emit = defineEmits<{ (e: 'deleted', id: string): void }>()

const markdown = computed(
  () =>
    'markdown' in props.item && props.item.markdown
      ? props.item.markdown
      : `[${props.item.title}](${props.item.url}) => ${props.item.translation}`,
)

async function copy() {
  try {
    await navigator.clipboard.writeText(markdown.value)
    toast.success('已复制 markdown')
  } catch {
    toast.error('复制失败（需 HTTPS 或授予剪贴板权限）')
  }
}

async function remove() {
  try {
    await api(`/api/collections/${props.item.id}`, { method: 'DELETE' })
    toast.success('已删除')
    emit('deleted', props.item.id)
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`删除失败：${err.message || '未知错误'}`)
  }
}
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-start justify-between gap-2">
        <CardTitle class="leading-snug">
          <a
            :href="item.url"
            target="_blank"
            rel="noopener noreferrer"
            class="text-primary hover:underline"
          >{{ item.title }}</a>
        </CardTitle>
        <Badge v-if="item.isFallbackTitle" variant="outline">标题兜底</Badge>
      </div>
      <CardDescription class="whitespace-normal">{{ item.translation }}</CardDescription>
    </CardHeader>
    <CardContent class="space-y-3">
      <pre class="overflow-x-auto rounded-lg bg-muted/60 p-3 text-xs leading-relaxed">{{ markdown }}</pre>
      <div class="flex gap-2">
        <Button size="sm" @click="copy">复制 markdown</Button>
        <Button v-if="showDelete !== false" size="sm" variant="outline" @click="remove">删除</Button>
      </div>
    </CardContent>
  </Card>
</template>