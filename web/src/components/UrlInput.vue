<script setup lang="ts">
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { api, type CandidateItem, type CollectResult } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'

const emit = defineEmits<{
  (e: 'collected', result: CollectResult): void
  (e: 'extracted', candidates: CandidateItem[]): void
}>()

const url = ref('')
const extract = ref(false)
const loading = ref(false)

async function save() {
  const trimmed = url.value.trim()
  if (!trimmed) {
    toast.error('请输入 URL')
    return
  }
  if (!/^https?:\/\//i.test(trimmed)) {
    toast.error('URL 需以 http:// 或 https:// 开头')
    return
  }
  loading.value = true
  try {
    if (extract.value) {
      const data = await api<{ candidates: CandidateItem[] }>('/api/collect/extract', {
        method: 'POST',
        body: JSON.stringify({ url: trimmed }),
      })
      if (!data.candidates?.length) toast.error('未从该页面提取到外链')
      else emit('extracted', data.candidates)
    } else {
      const data = await api<CollectResult | { url: string; error: string }>('/api/collect', {
        method: 'POST',
        body: JSON.stringify({ url: trimmed, extractLinks: false }),
      })
      if ('error' in data) toast.error(data.error)
      else {
        toast.success('已收藏')
        emit('collected', data)
      }
    }
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`操作失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <Card>
    <CardContent class="space-y-3 pt-4">
      <Input
        v-model="url"
        type="url"
        placeholder="粘贴 URL，例如 https://example.com/article"
        @keyup.enter="save"
      />
      <div class="flex items-center gap-4">
        <label class="flex cursor-pointer items-center gap-2 text-sm">
          <Checkbox v-model="extract" />
          提取本页内链接
        </label>
        <Button :disabled="loading" @click="save">{{ loading ? '处理中…' : '保存' }}</Button>
      </div>
    </CardContent>
  </Card>
</template>