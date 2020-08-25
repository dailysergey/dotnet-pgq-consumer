# dotnet-pgq-consumer
A PGQ Consumer written in DotNet Core 3.1 for postgres
# What's PGQ?
[PGQ](https://wiki.postgresql.org/wiki/PGQ_Tutorial) is the queueing solution from [Skytools](https://wiki.postgresql.org/wiki/SkyTools), which was written by [Skype](http://www.skype.com/en/). It's a neat way of writing database triggers that send events to an event queue in [PostgreSQL](http://www.postgresql.org/), which you can then poll with the PGQ API. An implementation of this polling is available in this library.
# Read more in habr post
E.g. How to install and create extension with pgq [post](https://habr.com/ru/post/483014/)
