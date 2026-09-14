# Orleans membership schema

These unmodified PostgreSQL scripts come from Microsoft Orleans 10.3.1, source
commit `137d9acc17830f15b13a4eb0058d6cee633cad5e`:

- [PostgreSQL-Main.sql](https://github.com/dotnet/orleans/blob/137d9acc17830f15b13a4eb0058d6cee633cad5e/src/AdoNet/Shared/PostgreSQL-Main.sql)
- [PostgreSQL-Clustering.sql](https://github.com/dotnet/orleans/blob/137d9acc17830f15b13a4eb0058d6cee633cad5e/src/AdoNet/Orleans.Clustering.AdoNet/PostgreSQL-Clustering.sql)

The upstream MIT license is included as `LICENSE.Orleans`. The scripts are
embedded resources, applied together as version 1 of `chess_orleans_schema` by
`InitializeOrleansStorage`. Future script changes require an explicit migration.
