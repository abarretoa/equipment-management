import type { HTMLAttributes } from 'react'
import { cn } from '../utils/cn'

export function PageContainer({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col gap-4 p-6', className)} {...props} />
}
