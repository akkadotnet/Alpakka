```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.3296/23H2/2023Update/SunValley3)
12th Gen Intel Core i7-1260P, 1 CPU, 16 logical and 12 physical cores
.NET SDK 8.0.101
  [Host]     : .NET 6.0.26 (6.0.2623.60508), X64 RyuJIT AVX2
  Job-HWCWUS : .NET 6.0.26 (6.0.2623.60508), X64 RyuJIT AVX2

IterationCount=5  RunStrategy=ColdStart  WarmupCount=0  

```
| Method            | Mean     | Error    | StdDev   |
|------------------ |---------:|---------:|---------:|
| RabbitMqWriteFlow | 9.294 μs | 3.906 μs | 1.014 μs |
