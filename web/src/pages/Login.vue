<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { api } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

const router = useRouter()
const password = ref('')
const loading = ref(false)

async function submit() {
  if (!password.value) return
  loading.value = true
  try {
    await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ password: password.value }) })
    toast.success('登录成功')
    router.push('/')
  } catch (e: unknown) {
    const err = e as { status?: number; message?: string }
    toast.error(err.status === 401 ? '密码错误' : `登录失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="flex min-h-svh items-center justify-center px-4">
    <Card class="w-full max-w-sm">
      <CardHeader>
        <CardTitle>linkstash 登录</CardTitle>
        <CardDescription>输入访问密码进入你的收藏库</CardDescription>
      </CardHeader>
      <CardContent class="space-y-3">
        <Input v-model="password" type="password" placeholder="访问密码" @keyup.enter="submit" />
        <Button class="w-full" :disabled="loading" @click="submit">登录</Button>
      </CardContent>
    </Card>
  </div>
</template>