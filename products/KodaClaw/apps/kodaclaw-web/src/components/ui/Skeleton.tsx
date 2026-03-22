type SkeletonProps = {
  height?: string | number;
  width?: string | number;
  className?: string;
  count?: number;
};

export function Skeleton({ height = 16, width = '100%', className, count = 1 }: SkeletonProps) {
  const style = {
    height: typeof height === 'number' ? `${height}px` : height,
    width: typeof width === 'number' ? `${width}px` : width,
  };

  if (count === 1) {
    return <div className={`skeleton ${className ?? ''}`} style={style} aria-hidden="true" />;
  }

  return (
    <div className="skeleton-stack">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className={`skeleton ${className ?? ''}`} style={style} aria-hidden="true" />
      ))}
    </div>
  );
}
