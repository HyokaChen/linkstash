<script setup lang="ts">
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { FileUpIcon, SearchIcon } from '@lucide/vue'
import { api, type ResolveResult } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import ImportPanel from '@/components/ImportPanel.vue'

const emit = defineEmits<{
  (e: 'resolved', result: ResolveResult): void
}>()

const url = ref('')
const loading = ref(false)
const importOpen = ref(false)
const inputEl = ref<InstanceType<typeof Input> | null>(null)

/**
 * 统一走 /api/collect/resolve：完整 URL、owner/repo 半截标识、关键词
 * 都能解析。这里不再做 /^https?:\/\// 前置校验——那是静默失败的来源。
 */
async function save() {
  const trimmed = url.value.trim()
  if (!trimmed) {
    toast.error('请输入内容')
    return
  }

  loading.value = true
  try {
    const data = await api<ResolveResult>('/api/collect/resolve', {
      method: 'POST',
      body: JSON.stringify({ input: trimmed }),
    })

    if (data.unresolved?.length) {
      const preview = data.unresolved.slice(0, 3).join('、')
      toast.warning(`未能自动解析：${preview}${data.unresolved.length > 3 ? ' 等' : ''}`)
    }

    if (!data.resolved?.length) {
      toast.error('未能解析出任何网址')
      return
    }

    emit('resolved', data)
    url.value = ''
    inputEl.value?.$el.querySelector('input')?.focus()
  } catch (e: unknown) {
    const err = e as { message?: string; status?: number }
    // 400 携带后端给出的 error 文案（含未识别输入列表）
    if (err.status === 400) {
      toast.error(err.message?.slice(0, 80) || '未能解析出任何网址')
    } else {
      toast.error(`操作失败：${err.message || '未知错误'}`)
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <Card>
    <CardContent class="pt-4">
      <form class="flex gap-2" @submit.prevent="save">
        <div class="relative flex-1">
          <SearchIcon
            class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
          />
          <Input
            ref="inputEl"
            v-model="url"
            inputmode="url"
            autocomplete="off"
            spellcheck="false"
            class="pl-8"
            placeholder="粘贴链接、owner/repo 或关键词，回车收藏"
            :disabled="loading"
          />
        </div>
        <Button type="submit" :disabled="loading || !url.trim()">
          {{ loading ? '处理中…' : '收藏' }}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="icon"
          title="导入浏览器书签"
          :disabled="loading"
          @click="importOpen = true"
        >
          <FileUpIcon class="size-4" />
          <span class="sr-only">导入书签</span>
        </Button>
      </form>
    </CardContent>
  </Card>

  <ImportPanel v-model:open="importOpen" @parsed="emit('resolved', $event)" />
</template>
