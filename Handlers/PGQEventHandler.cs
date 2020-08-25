namespace DotNetPGMQ
{
    public interface IPGQEventHandler
    {
        public void Handle(PGQEvent ev);
    }
}