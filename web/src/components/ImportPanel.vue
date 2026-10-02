<script setup lang="ts">
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import { UploadIcon } from '@lucide/vue'
import {
  api,
  type ImportPreviewResult,
  type ResolveResult,
  type ResolveCandidate,
} from '@/api/client'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Textarea } from '@/components/ui/textarea'

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ (e: 'parsed', result: ResolveResult): void }>()

const html = ref('')
const groupName = ref('')
const parsing = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)

async function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  html.value = await file.text()
  if (!groupName.value) groupName.value = file.name.replace(/\.html?$/i, '')
  input.value = ''
}

async function parse() {
  if (!html.value.trim()) {
    toast.error('请选择书签文件或粘贴其 HTML 内容')
    return
  }

  parsing.value = true
  try {
    const data = await api<ImportPreviewResult>('/api/import/bookmarks', {
      method: 'POST',
      body: JSON.stringify({ html: html.value, groupName: groupName.value || null }),
    })

    if (!data.candidates?.length) {
      toast.error('没有可导入的新条目（可能全部已收藏）')
      return
    }

    // 复用 BatchPreview：把书签条目转成 resolve 结果结构，集合 id 挂在 groupId
    const rows: ResolveCandidate[] = data.candidates.map((c) => ({
      input: c.folderPath || '书签',
      url: c.url,
      title: c.title || c.url,
      translation: '',
      isFallbackTitle: !c.title,
      tags: '',
      isExisting: c.isExisting,
    }))

    emit('parsed', {
      resolved: rows,
      unresolved: [],
      // groupId 通过 result 上的额外字段传给 BatchPreview
      ...({ groupId: data.groupId, groupName: data.name, skipped: data.skipped } as object),
    } as ResolveResult & { groupId?: string; groupName?: string; skipped?: number })

    open.value = false
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`解析失败：${err.message?.slice(0, 80) || '未知错误'}`)
  } finally {
    parsing.value = false
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-w-2xl">
      <DialogHeader>
        <DialogTitle>导入浏览器书签</DialogTitle>
        <DialogDescription>
          支持 Chrome / Edge / Firefox 导出的 .html 书签文件。解析后可勾选导入。
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-3">
        <div class="flex items-center gap-2">
          <input
            ref="fileInput"
            type="file"
            accept=".html,.htm"
            class="hidden"
            @change="onFileChange"
          />
          <Button variant="outline" @click="fileInput?.click()">
            <UploadIcon class="size-4" />
            选择书签文件
          </Button>
          <span class="text-xs text-muted-foreground">或直接粘贴下面的 HTML 内容</span>
        </div>

        <Textarea
          v-model="html"
          class="min-h-32 font-mono text-xs"
          placeholder="<DT><A HREF=&quot;https://example.com&quot;>example</A>"
        />

        <div>
          <label class="mb-1 block text-xs text-muted-foreground">集合名称（可选）</label>
          <input
            v-model="groupName"
            class="border-input h-8 w-full rounded-lg border bg-transparent px-2.5 text-sm outline-none focus-visible:ring-3"
            placeholder="留空则使用「书签导入 日期」"
          />
        </div>
      </div>

      <DialogFooter>
        <Button :disabled="parsing" @click="parse">
          {{ parsing ? '解析中…' : '解析' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
