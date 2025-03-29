
namespace CSLua;

// grep `NoTagMethodFlags' if num of TMS >= 32
internal enum TMS: byte
{
	TM_INDEX,
	TM_NEWINDEX,
	TM_GC,
	TM_MODE,
	TM_LEN,
	TM_EQ,
	TM_ADD,
	TM_SUB,
	TM_MUL,
	TM_DIV,
	TM_MOD,
	TM_POW,
	TM_UNM,
	TM_LT,
	TM_LE,
	TM_CONCAT,
	TM_CALL,
}